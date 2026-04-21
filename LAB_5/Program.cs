using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace FileStorage
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();

            // Корневая папка хранилища
            string storageRoot = Path.Combine(Directory.GetCurrentDirectory(), "Storage");
            Directory.CreateDirectory(storageRoot);

            // Защита от выхода за пределы хранилища
            string GetSafePath(string requestPath)
            {
                string relativePath = requestPath.TrimStart('/');
                string fullPath = Path.GetFullPath(Path.Combine(storageRoot, relativePath));

                if (!fullPath.StartsWith(storageRoot))
                    throw new BadHttpRequestException("Недопустимый путь", 400);

                return fullPath;
            }

            // GET для корня (путь не указан)
            app.MapGet("/", () =>
            {
                string fullPath = GetSafePath("");
                var items = Directory.GetFileSystemEntries(fullPath).Select(item => new
                {
                    Name = Path.GetFileName(item),
                    IsDirectory = Directory.Exists(item),
                    Size = File.Exists(item) ? new FileInfo(item).Length : 0,
                    LastModified = File.GetLastWriteTimeUtc(item)
                });

                return Results.Ok(items);
            });

            // GET - получение файла или списка каталога
            app.MapGet("/{**path}", (string path) =>
            {
                string fullPath = GetSafePath(path);

                // Если файл - отдаём его
                if (File.Exists(fullPath))
                    return Results.File(fullPath);

                // Если каталог - отдаём список файлов и папок в JSON
                if (Directory.Exists(fullPath))
                {
                    var items = Directory.GetFileSystemEntries(fullPath).Select(item => new
                    {
                        Name = Path.GetFileName(item),
                        IsDirectory = Directory.Exists(item),
                        Size = File.Exists(item) ? new FileInfo(item).Length : 0,
                        LastModified = File.GetLastWriteTimeUtc(item)
                    });

                    return Results.Ok(items);
                }

                return Results.NotFound();
            });

            // PUT - загрузка файла с перезаписью
            app.MapPut("/{**path}", async (HttpRequest request, string path) =>
            {
                if (string.IsNullOrEmpty(path))
                    return Results.BadRequest("Путь к файлу не указан");

                string fullPath = GetSafePath(path);

                // Создаём директорию если её нет
                string? directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                // Проверяем существовал ли файл
                bool fileExists = File.Exists(fullPath);

                // Сохраняем файл (перезаписываем если существует)
                using var fileStream = new FileStream(fullPath, FileMode.Create);
                await request.Body.CopyToAsync(fileStream);

                // 201 Created - если создан новый файл, 200 OK - если перезаписан
                return fileExists ? Results.Ok() : Results.Created($"/{path}", null);
            });

            // HEAD - получение информации о файле (только заголовки)
            app.MapMethods("/{**path}", new[] { "HEAD" }, (HttpContext context, string path) =>
            {
                if (string.IsNullOrEmpty(path))
                    return Results.BadRequest();

                string fullPath = GetSafePath(path);

                if (!File.Exists(fullPath))
                    return Results.NotFound();

                var fileInfo = new FileInfo(fullPath);
                context.Response.Headers.ContentLength = fileInfo.Length;
                context.Response.Headers["Last-Modified"] = fileInfo.LastWriteTimeUtc.ToString("R");

                return Results.Ok();
            });

            // DELETE - удаление файла или каталога
            app.MapDelete("/{**path}", (string path) =>
            {
                if (string.IsNullOrEmpty(path))
                    return Results.BadRequest();

                string fullPath = GetSafePath(path);

                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    return Results.NoContent();
                }

                if (Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, true);
                    return Results.NoContent();
                }

                return Results.NotFound();
            });

            app.Run("http://localhost:5000");
        }
    }
}