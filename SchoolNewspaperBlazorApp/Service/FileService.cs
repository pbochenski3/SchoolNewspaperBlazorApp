using Microsoft.AspNetCore.Components.Forms;
using SchoolNewspaperBlazorApp.Data;
using SchoolNewspaperBlazorApp.Interfaces.Repository;
using SchoolNewspaperBlazorApp.Interfaces.Service;

namespace SchoolNewspaperBlazorApp.Service
{
    public class FileService : IFileService
    {
        private readonly IFileRepository _fileRepository;
        public FileService(IFileRepository fileRepository)
        {
            _fileRepository = fileRepository;
        }
        private readonly string uploadPath = @"C:\Users\Praktyka\source\repos\SchoolNewspaperBlazorApp\Images";
        public async Task<string> GetPreviewAsync(IBrowserFile file)
        {
            using var stream = file.OpenReadStream(5 * 1024 * 1024);
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            byte[] bytes = memoryStream.ToArray();
            return $"data:{file.ContentType};base64,{Convert.ToBase64String(bytes)}";
        }
        public async Task<int> UploadImage(IBrowserFile file)
        {
            //Tworzenie folderu
            Directory.CreateDirectory(uploadPath);
            //Pobieranie extension z pliku czyli np .png
            string extension = Path.GetExtension(file.Name);
            //Generowanie nazwy + extension np. plik.png
            string fileName = $"{Guid.NewGuid()}{extension}";
            // łaczenie uploadPath i fileName C:\Users\Praktyka\source\repos\SchoolNewspaperBlazorApp\Images\plik.png
            string fullPath = Path.Combine(uploadPath, fileName);

            using var stream = file.OpenReadStream(5 * 1024 * 1024);
            using var fileStream = new FileStream(
                fullPath, //pelny link odnosnika
                FileMode.Create //ustawienie tworzenia
                );
            await stream.CopyToAsync(fileStream);
            var id = await _fileRepository.GetLastFileId();
            var mediaFile = new MediaFile
            {
                Id = id,
                FileName = fileName,
                FileType = extension
            };
            await _fileRepository.AddFileAsync(mediaFile);

            return id;



        }
    }
}
