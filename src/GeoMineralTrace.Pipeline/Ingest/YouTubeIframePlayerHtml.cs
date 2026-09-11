using System.Text;

namespace GeoMineralTrace.Pipeline.Ingest;

/// <summary>
/// YouTube IFrame Player API + Screen Capture / MediaRecorder for WebView2.
/// </summary>
public static class YouTubeIframePlayerHtml
{
    public static string Build(string videoId) => BuildPlayerPage(videoId);

    public static string BuildPlayerPage(string videoId)
    {
        var id = videoId.Trim();
        var sb = new StringBuilder();
        sb.Append("""
            <!DOCTYPE html>
            <html><head>
            <meta charset="utf-8"/>
            <meta name="viewport" content="width=device-width, initial-scale=1"/>
            <style>
            html,body{margin:0;height:100%;background:#0f0f12;overflow:hidden;font-family:Segoe UI,sans-serif}
            #player{width:100%;height:100%}
            #overlay{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;
            color:#ccc;background:#0f0f12;font-size:14px;z-index:2}
            #wrap{position:relative;width:100%;height:100%}
            #recBadge{position:absolute;top:8px;left:8px;z-index:5;display:none;align-items:center;gap:6px;
            background:rgba(180,0,0,0.85);color:#fff;padding:4px 10px;border-radius:4px;font-size:12px;font-weight:600}
            #recDot{width:8px;height:8px;border-radius:50%;background:#fff;animation:blink 1s infinite}
            @keyframes blink{50%{opacity:0.3}}
            </style>
            <script src="https://www.youtube.com/iframe_api"></script>
            </head><body>
            <div id="wrap">
            <div id="recBadge"><div id="recDot"></div><span id="recLabel">REC</span></div>
            <div id="player"></div>
            <div id="overlay">Loading player…</div>
            </div>
            <script>
            var player=null,loopStart=null,loopEnd=null,loopTimer=null;
            var captureStream=null,mediaRecorder=null,recordedChunks=[],recMime='video/webm';
            var recState='idle',recStartedAt=0,recTimer=null,autoStopAt=null,autoStopActive=false;
            // Post a real object — JSON.stringify would double-encode and break C# WebMessageAsJson parsing.
            function post(o){try{window.chrome.webview.postMessage(o);}catch(e){}}
            function hideOverlay(){var o=document.getElementById('overlay');if(o)o.style.display='none';}
            function showOverlay(t){var o=document.getElementById('overlay');if(o){o.textContent=t;o.style.display='flex';}}
            function showRecBadge(on,text){
              var b=document.getElementById('recBadge'),l=document.getElementById('recLabel');
              if(b)b.style.display=on?'flex':'none';
              if(l&&text)l.textContent=text;
            }
            function pickMime(){
              var c=['video/webm;codecs=vp9,opus','video/webm;codecs=vp8,opus','video/webm;codecs=vp8','video/webm','video/mp4'];
              for(var i=0;i<c.length;i++) if(window.MediaRecorder&&MediaRecorder.isTypeSupported(c[i])) return c[i];
              return 'video/webm';
            }
            async function sendBlob(blob){
              var buf=await blob.arrayBuffer();
              var bytes=new Uint8Array(buf),chunkSize=384*1024,total=bytes.length;
              var chunks=Math.ceil(total/chunkSize)||1;
              post({type:'recordingStart',mimeType:blob.type||recMime,totalBytes:total,totalChunks:chunks});
              for(var i=0;i<total;i+=chunkSize){
                var slice=bytes.subarray(i,Math.min(i+chunkSize,total));
                var bin=''; for(var j=0;j<slice.length;j++) bin+=String.fromCharCode(slice[j]);
                post({type:'recordingChunk',index:Math.floor(i/chunkSize),data:btoa(bin)});
              }
              post({type:'recordingComplete'});
            }
            function stopRecTimer(){if(recTimer){clearInterval(recTimer);recTimer=null;}}
            function startRecTimer(){
              stopRecTimer();
              recTimer=setInterval(function(){
                if(recState!=='recording') return;
                var sec=Math.floor((Date.now()-recStartedAt)/1000);
                var m=Math.floor(sec/60),s=sec%60;
                showRecBadge(true,'REC '+m+':'+(s<10?'0':'')+s);
                post({type:'recordingProgress',elapsedSeconds:sec});
              },500);
            }
            async function startCapture(opts){
              opts=opts||{};
              if(recState!=='idle'){post({type:'captureError',message:'Already recording.'});return;}
              try{
                if(!navigator.mediaDevices||!navigator.mediaDevices.getDisplayMedia){
                  post({type:'captureError',message:'Screen Capture API is not available in this WebView.'});
                  return;
                }
                var constraints={
                  video:{displaySurface:'browser',cursor:'always'},
                  audio:true,
                  preferCurrentTab:true,
                  selfBrowserSurface:'include',
                  systemAudio:'include'
                };
                captureStream=await navigator.mediaDevices.getDisplayMedia(constraints);
                captureStream.getVideoTracks()[0].onended=function(){
                  if(recState==='recording'||recState==='paused') stopCapture();
                };
                recMime=pickMime();
                mediaRecorder=new MediaRecorder(captureStream,{mimeType:recMime});
                recordedChunks=[];
                mediaRecorder.ondataavailable=function(e){if(e.data&&e.data.size)recordedChunks.push(e.data);};
                mediaRecorder.onstop=async function(){
                  recState='idle'; stopRecTimer(); showRecBadge(false);
                  autoStopActive=false; autoStopAt=null;
                  try{
                    var blob=new Blob(recordedChunks,{type:recMime});
                    if(blob.size<1024){
                      post({type:'captureError',message:'Recording was empty — select the player window and play the video while recording.'});
                    } else {
                      await sendBlob(blob);
                    }
                  }catch(err){post({type:'captureError',message:err.message||String(err)});}
                  if(captureStream){captureStream.getTracks().forEach(function(t){t.stop();}); captureStream=null;}
                  mediaRecorder=null; recordedChunks=[];
                  post({type:'captureStopped'});
                };
                mediaRecorder.onstart=function(){
                  recState='recording'; recStartedAt=Date.now(); startRecTimer();
                  post({type:'captureStarted',mimeType:recMime});
                };
                mediaRecorder.start(1000);
                if(typeof opts.autoPlayFrom==='number'&&player){
                  player.seekTo(opts.autoPlayFrom,true);
                  player.playVideo();
                }
                if(typeof opts.autoStopAt==='number'){
                  autoStopAt=opts.autoStopAt;
                  autoStopActive=true;
                }
              }catch(err){
                recState='idle'; showRecBadge(false);
                if(err.name==='NotAllowedError'||err.name==='AbortError')
                  post({type:'captureDenied',message:'Screen capture was cancelled or denied. Choose this app window or the player tab when prompted.'});
                else post({type:'captureError',message:err.message||String(err)});
              }
            }
            function pauseCapture(){
              if(recState!=='recording'||!mediaRecorder)return;
              mediaRecorder.pause(); recState='paused';
              stopRecTimer();
              showRecBadge(true,'PAUSED');
              post({type:'capturePaused'});
            }
            function resumeCapture(){
              if(recState!=='paused'||!mediaRecorder)return;
              mediaRecorder.resume(); recState='recording';
              recStartedAt=Date.now()-((recordedChunks.length||1)*1000);
              startRecTimer();
              post({type:'captureResumed'});
            }
            function stopCapture(){
              if(!mediaRecorder||recState==='idle')return;
              try{mediaRecorder.stop();}catch(e){}
            }
            function onYouTubeIframeAPIReady(){
              player=new YT.Player('player',{
                videoId:'
            """);
        sb.Append(EscapeJs(id));
        sb.Append("""
            ',
                playerVars:{rel:0,modestbranding:1,playsinline:1,origin:'https://www.youtube.com'},
                events:{
                  onReady:function(e){
                    hideOverlay();
                    var d=e.target.getDuration();
                    post({type:'ready',duration:d,isLive:e.target.getVideoData().isLive||false});
                  },
                  onStateChange:function(e){
                    post({type:'state',state:e.data,current:player?player.getCurrentTime():0});
                    if(e.data===YT.PlayerState.PLAYING&&loopStart!=null&&loopEnd!=null) startLoopWatch();
                    if(e.data===YT.PlayerState.PAUSED||e.data===YT.PlayerState.ENDED) stopLoopWatch();
                  },
                  onError:function(e){
                    var msg='Video failed to load';
                    if(e.data===2)msg='Invalid video ID';
                    else if(e.data===5)msg='HTML5 playback error';
                    else if(e.data===100)msg='Video unavailable or private';
                    else if(e.data===101||e.data===150)msg='Embedding disabled by the uploader';
                    else if(e.data===103)msg='Video blocked on external sites';
                    showOverlay(msg);
                    post({type:'error',code:e.data,message:msg});
                  }
                }
              });
            }
            function startLoopWatch(){
              stopLoopWatch();
              loopTimer=setInterval(function(){
                if(!player||loopStart==null||loopEnd==null)return;
                var t=player.getCurrentTime();
                if(t>=loopEnd-0.05){player.seekTo(loopStart,true);player.playVideo();}
              },100);
            }
            function stopLoopWatch(){if(loopTimer){clearInterval(loopTimer);loopTimer=null;}}
            if(window.chrome&&window.chrome.webview){
              window.chrome.webview.addEventListener('message',function(ev){
                // Host PostWebMessageAsJson delivers an object; string payloads still need parse.
                var cmd=ev.data;
                if(typeof cmd==='string'){ try{cmd=JSON.parse(cmd);}catch(x){return;} }
                if(!cmd||typeof cmd!=='object')return;
                if(cmd.command==='startCapture'){startCapture(cmd);return;}
                if(cmd.command==='pauseCapture'){pauseCapture();return;}
                if(cmd.command==='resumeCapture'){resumeCapture();return;}
                if(cmd.command==='stopCapture'){stopCapture();return;}
                if(!player||!player.getDuration)return;
                if(cmd.command==='play')player.playVideo();
                else if(cmd.command==='pause')player.pauseVideo();
                else if(cmd.command==='seek'&&typeof cmd.seconds==='number')player.seekTo(cmd.seconds,true);
                else if(cmd.command==='loopPreview'){
                  loopStart=cmd.start; loopEnd=cmd.end;
                  player.seekTo(loopStart,true); player.playVideo();
                }
                else if(cmd.command==='stopLoop'){loopStart=null;loopEnd=null;stopLoopWatch();}
              });
            }
            setInterval(function(){
              if(!player||!player.getCurrentTime)return;
              var cur=player.getCurrentTime();
              post({type:'tick',current:cur,duration:player.getDuration(),recording:recState==='recording'});
              if(autoStopActive&&autoStopAt!=null&&cur>=autoStopAt-0.05&&recState==='recording') stopCapture();
            },250);
            </script></body></html>
            """);
        return sb.ToString();
    }

    private static string EscapeJs(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
}
