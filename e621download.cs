using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class Program
{
    private static HttpClient client = new HttpClient();
    private static string baseUrl = "https://e621.net";
    private static StreamWriter logWriter = null;
    
    public static async Task Main(string[] args)
    {
        Console.Title = "E621.NET Downloader";
        
        string logFileName = $"e621_download_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        logWriter = new StreamWriter(logFileName, true);
        
        Console.WriteLine("=== E621.NET Downloader ===\n");
        LogMessage($"=== Запуск программы: {DateTime.Now} ===");
        
        client.DefaultRequestHeaders.Add("User-Agent", "MyDownloaderApp/1.0 (by YourUsername)");
        
        try
        {
            Console.WriteLine("Что вы хотите скачать?");
            Console.WriteLine("1. Posts (по тегам)");
            Console.WriteLine("2. Pools (коллекции)");
            Console.WriteLine("3. Сохранения юзеров (избранное)");
            Console.Write("Ваш выбор (1, 2 или 3): ");
            
            string downloadTypeChoice = Console.ReadLine();
            string searchQuery = "";
            string downloadTypeName = "";
            string folderIdentifier = "";
            
            switch (downloadTypeChoice)
            {
                case "1":
                    Console.Write("\nВведите теги для поиска: ");
                    searchQuery = Console.ReadLine();
                    downloadTypeName = "1";
                    folderIdentifier = CleanFileName(searchQuery.Replace(" ", "_"));
                    LogMessage($"Выбраны Posts по тегам: {searchQuery}");
                    break;
                    
                case "2":
                    Console.Write("\nВведите ссылку на pool: ");
                    string poolUrl = Console.ReadLine();
                    
                    Match poolMatch = Regex.Match(poolUrl, @"/pools/(\d+)");
                    if (poolMatch.Success)
                    {
                        string poolId = poolMatch.Groups[1].Value;
                        searchQuery = $"pool:{poolId}";
                        downloadTypeName = "2";
                        folderIdentifier = $"pool_{poolId}";
                        LogMessage($"Выбран Pool: {poolUrl}");
                    }
                    else
                    {
                        Console.WriteLine("Неверная ссылка на pool!");
                        LogMessage($"Ошибка: неверная ссылка на pool - {poolUrl}");
                        Console.ReadLine();
                        return;
                    }
                    break;
                    
                case "3":
                    Console.Write("\nВведите юзернейм: ");
                    string username = Console.ReadLine();
                    searchQuery = $"fav:{username}";
                    downloadTypeName = "3";
                    folderIdentifier = CleanFileName(username);
                    LogMessage($"Выбраны избранные пользователя: {username}");
                    break;
                    
                default:
                    Console.WriteLine("Неверный выбор!");
                    LogMessage($"Неверный выбор типа скачивания: {downloadTypeChoice}");
                    Console.ReadLine();
                    return;
            }
            
            if (string.IsNullOrWhiteSpace(searchQuery))
            {
                Console.WriteLine("Ошибка: запрос пустой!");
                LogMessage("Ошибка: пустой поисковый запрос");
                Console.ReadLine();
                return;
            }

            Console.WriteLine("\nВыберите тип контента:");
            Console.WriteLine("1. Фотки/Картинки (jpg, png, gif)");
            Console.WriteLine("2. Видео (webm, mp4)");
            Console.WriteLine("3. Всё (и фото и видео)");
            Console.Write("Ваш выбор (1, 2 или 3): ");
            
            string contentTypeChoice = Console.ReadLine();
            string contentType = "image";
            string folderTypeName = "фото";
            
            if (contentTypeChoice == "1")
            {
                contentType = "image";
                folderTypeName = "фото";
                Console.WriteLine("Выбраны изображения");
                LogMessage("Выбран тип: изображения");
            }
            else if (contentTypeChoice == "2")
            {
                contentType = "video";
                folderTypeName = "видео";
                Console.WriteLine("Выбраны видео");
                LogMessage("Выбран тип: видео");
            }
            else if (contentTypeChoice == "3")
            {
                contentType = "all";
                folderTypeName = "все";
                Console.WriteLine("Выбрано всё");
                LogMessage("Выбран тип: всё");
            }
            else
            {
                Console.WriteLine("Неверный выбор, использую изображения по умолчанию");
                LogMessage("Неверный выбор типа контента, используется изображения по умолчанию");
            }

            Console.Write("\nСколько постов скачать? (макс 320, реком. 50): ");
            string limitInput = Console.ReadLine();
            int limit = 50;
            
            if (int.TryParse(limitInput, out int parsedLimit) && parsedLimit > 0 && parsedLimit <= 320)
            {
                limit = parsedLimit;
            }
            else
            {
                Console.WriteLine($"Использую лимит по умолчанию: {limit}");
            }
            
            LogMessage($"Лимит скачивания: {limit}");

            string folderName = $"e621_{downloadTypeName}_{folderTypeName}_{folderIdentifier}_{limit}";

            if (folderName.Length > 100)
            {
                folderName = folderName.Substring(0, 100);
            }

            string currentDir = Directory.GetCurrentDirectory();
            string downloadFolder = Path.Combine(currentDir, folderName);

            int counter = 1;
            string originalFolder = downloadFolder;
            while (Directory.Exists(downloadFolder))
            {
                downloadFolder = originalFolder + "_" + counter;
                counter++;
            }
            
            Directory.CreateDirectory(downloadFolder);
            Console.WriteLine($"\nПапка создана: {downloadFolder}");
            LogMessage($"Папка для скачивания: {downloadFolder}");

            Console.WriteLine($"\nИщу контент: {searchQuery}...");
            LogMessage($"Поиск контента: {searchQuery}");
            
            var posts = await GetPosts(searchQuery, limit);
            
            if (posts == null || posts.Count == 0)
            {
                Console.WriteLine("Посты не найдены!");
                LogMessage("Посты не найдены!");
                Console.ReadLine();
                return;
            }
            
            Console.WriteLine($"Найдено постов: {posts.Count}");
            LogMessage($"Найдено постов: {posts.Count}");

            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine("СТАТУС СКАЧИВАНИЯ:");
            Console.WriteLine(new string('=', 50));
            
            int downloadedCount = 0;
            int skippedCount = 0;
            int failedCount = 0;

            for (int i = 0; i < posts.Count; i++)
            {
                var post = posts[i];
                string fileUrl = GetFileUrl(post, contentType);
                
                if (string.IsNullOrEmpty(fileUrl))
                {
                    skippedCount++;
                    LogMessage($"[ПРОПУЩЕНО] Пост {post.Id}: не подходит под выбранный тип");
                    continue;
                }
                
                string fileName = $"{post.Id}_{Path.GetFileName(new Uri(fileUrl).LocalPath)}";
                string filePath = Path.Combine(downloadFolder, fileName);
                
                if (File.Exists(filePath))
                {
                    Console.WriteLine($"[{i + 1}/{posts.Count}] ✓ Уже существует: {fileName}");
                    LogMessage($"[УЖЕ ЕСТЬ] {fileName}");
                    skippedCount++;
                    continue;
                }
                
                Console.Write($"[{i + 1}/{posts.Count}] Скачиваю: {fileName}... ");
                
                if (await DownloadFile(fileUrl, filePath))
                {
                    Console.WriteLine("✓ УСПЕХ");
                    LogMessage($"[УСПЕХ] {fileName}");
                    downloadedCount++;
                }
                else
                {
                    Console.WriteLine("✗ ОШИБКА");
                    LogMessage($"[ОШИБКА] {fileName}");
                    failedCount++;
                }

                await Task.Delay(1000);
            }

            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine("ИТОГИ СКАЧИВАНИЯ:");
            Console.WriteLine(new string('=', 50));
            Console.WriteLine($"✓ Успешно скачано: {downloadedCount} файлов");
            Console.WriteLine($"↷ Пропущено: {skippedCount} файлов");
            Console.WriteLine($"✗ Ошибок: {failedCount} файлов");
            Console.WriteLine($"📁 Папка: {downloadFolder}");
            Console.WriteLine($"📋 Лог файл: {logFileName}");
            Console.WriteLine(new string('=', 50));
            
            LogMessage($"=== ИТОГИ ===");
            LogMessage($"Успешно: {downloadedCount}");
            LogMessage($"Пропущено: {skippedCount}");
            LogMessage($"Ошибок: {failedCount}");
            LogMessage($"Папка: {downloadFolder}");
            LogMessage($"=== Завершено: {DateTime.Now} ===");
            
            if (downloadedCount > 0)
            {
                Console.Write("\nОткрыть папку с файлами? (y/n): ");
                string openFolder = Console.ReadLine();
                if (openFolder != null && openFolder.ToLower() == "y")
                {
                    System.Diagnostics.Process.Start("explorer.exe", downloadFolder);
                    LogMessage("Открыта папка с файлами");
                }
            }
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n✗ КРИТИЧЕСКАЯ ОШИБКА: {ex.Message}");
            LogMessage($"КРИТИЧЕСКАЯ ОШИБКА: {ex.Message}");
        }
        finally
        {
            logWriter?.Close();
        }
        
        Console.WriteLine("\nНажмите Enter для выхода...");
        Console.ReadLine();
    }
    
    static void LogMessage(string message)
    {
        try
        {
            string logEntry = $"[{DateTime.Now:HH:mm:ss}] {message}";
            logWriter?.WriteLine(logEntry);
            logWriter?.Flush();
        }
        catch
        {

        }
    }
    
    static async Task<List<PostData>> GetPosts(string tags, int limit)
    {
        try
        {
            string url = $"{baseUrl}/posts.json?tags={Uri.EscapeDataString(tags)}&limit={limit}";
            LogMessage($"Запрос к API: {url}");
            
            HttpResponseMessage response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            string json = await response.Content.ReadAsStringAsync();
            LogMessage($"Получен ответ от API, размер: {json.Length} байт");
            
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement;
                var posts = new List<PostData>();
                
                if (root.TryGetProperty("posts", out JsonElement postsElement))
                {
                    foreach (JsonElement postElement in postsElement.EnumerateArray())
                    {
                        var post = new PostData();
                        
                        if (postElement.TryGetProperty("id", out JsonElement idElement))
                            post.Id = idElement.GetInt32();
                        
                        if (postElement.TryGetProperty("file", out JsonElement fileElement))
                        {
                            if (fileElement.TryGetProperty("url", out JsonElement urlElement))
                            {
                                post.FileUrl = urlElement.GetString();
                            }
                        }
                        
                        posts.Add(post);
                    }
                }
                
                return posts;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка получения постов: {ex.Message}");
            LogMessage($"Ошибка получения постов: {ex.Message}");
            return new List<PostData>();
        }
    }
    
    static string GetFileUrl(PostData post, string contentType)
    {
        if (string.IsNullOrEmpty(post.FileUrl))
            return null;
        
        string url = post.FileUrl.ToLower();
        
        if (contentType == "image")
        {
            if (url.EndsWith(".jpg") || url.EndsWith(".jpeg") || url.EndsWith(".png") || url.EndsWith(".gif"))
            {
                return post.FileUrl;
            }
        }
        else if (contentType == "video")
        {
            if (url.EndsWith(".webm") || url.EndsWith(".mp4") || url.EndsWith(".mov") || url.EndsWith(".avi"))
            {
                return post.FileUrl;
            }
        }
        else if (contentType == "all")
        {
            if (url.EndsWith(".jpg") || url.EndsWith(".jpeg") || url.EndsWith(".png") || url.EndsWith(".gif") ||
                url.EndsWith(".webm") || url.EndsWith(".mp4") || url.EndsWith(".mov") || url.EndsWith(".avi"))
            {
                return post.FileUrl;
            }
        }
        
        return null;
    }
    
    static async Task<bool> DownloadFile(string url, string filePath)
    {
        try
        {
            HttpResponseMessage response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            using (FileStream fs = new FileStream(filePath, FileMode.Create))
            {
                await response.Content.CopyToAsync(fs);
            }
            
            return true;
        }
        catch (Exception ex)
        {
            LogMessage($"Ошибка загрузки файла {url}: {ex.Message}");
            return false;
        }
    }
    
    static string CleanFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return "unknown";
        
        char[] invalidChars = Path.GetInvalidFileNameChars();
        foreach (char c in invalidChars)
        {
            fileName = fileName.Replace(c.ToString(), "");
        }
        
        if (fileName.Length > 30)
            fileName = fileName.Substring(0, 30);
        
        return fileName;
    }
    
    class PostData
    {
        public int Id { get; set; }
        public string FileUrl { get; set; }
    }
}