using GeoMineralTrace.Solar.Knowledge;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace GeoMineralTrace_App.Pages;

public sealed partial class TechniquesPage : Page
{
    private readonly TechniquesKnowledgeBase _kb;

    public TechniquesPage()
    {
        InitializeComponent();
        _kb = App.Services.GetRequiredService<TechniquesKnowledgeBase>();
        RefreshList();
    }

    private void RefreshList()
    {
        var articles = _kb.Search(SearchBox.Text);
        ArticleList.ItemsSource = articles.Select(a => a.Title).ToList();
        if (articles.Count > 0)
        {
            ArticleList.SelectedIndex = 0;
            BodyBlock.Text = articles[0].BodyMarkdown;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();

    private void ArticleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ArticleList.SelectedItem is not string title)
            return;
        var article = _kb.All.FirstOrDefault(a => a.Title == title);
        if (article is not null)
            BodyBlock.Text = article.BodyMarkdown;
    }
}
