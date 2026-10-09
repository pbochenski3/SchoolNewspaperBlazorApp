using SchoolNewspaperBlazorApp.Data;

namespace SchoolNewspaperBlazorApp.Interfaces.Repository
{
    public interface IFileRepository
    {
        Task<int> GetLastFileId();
        Task AddFileAsync(MediaFile file);
        Task<MediaFile> GetFileByIdAsync(int id);
    }
}
