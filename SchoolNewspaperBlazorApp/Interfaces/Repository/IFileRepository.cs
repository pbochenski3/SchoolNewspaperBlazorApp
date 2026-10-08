namespace SchoolNewspaperBlazorApp.Interfaces.Repository
{
    public interface IFileRepository
    {
        Task<int> GetLastFileId();
        Task AddFileAsync(Data.MediaFile file);
    }
}
