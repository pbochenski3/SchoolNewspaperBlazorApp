using SchoolNewspaperBlazorApp.Data;

namespace SchoolNewspaperBlazorApp.Interfaces.Service
{
    public interface IArticleService
    {
        Task AddArticleAsync(string title, string text, string author, int id);
        Task<List<Article>> GetAllArticlesAsync();
        Task<Article> GetArticleByIdAsync(int id);
        Task RemoveArticleByIdAsync(int id);
    }
}
