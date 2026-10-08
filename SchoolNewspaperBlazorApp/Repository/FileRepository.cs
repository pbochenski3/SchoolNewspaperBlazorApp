using Microsoft.EntityFrameworkCore;
using SchoolNewspaperBlazorApp.Data;
using SchoolNewspaperBlazorApp.Interfaces.Repository;

namespace SchoolNewspaperBlazorApp.Repository
{
    public class FileRepository : IFileRepository
    {
        private readonly NewspaperDbContext _context;
        public FileRepository(NewspaperDbContext context)
        {
            _context = context;
        }
        public async Task<int> GetLastFileId()
        {
            var id = await _context.Files
                .OrderByDescending(x => x.Id)
                .Select(x => x.Id)
                .FirstOrDefaultAsync();
            return id++;
        }
        public async Task AddFileAsync(Data.MediaFile file)
        {
            await _context.Files.AddAsync(file);
            await _context.SaveChangesAsync();
        }

    }
}
