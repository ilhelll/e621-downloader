using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Globalization;

public class Program
{
    private static HttpClient client = new HttpClient();
    private static string baseUrl = "https://e621.net";
    private static StreamWriter logWriter = null;
    private static Dictionary<string, string> lang = new Dictionary<string, string>();

    public static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        
        Console.WriteLine("Выберите язык / Select language:");
        Console.WriteLine("[1] Русский");
        Console.WriteLine("[2] English");
        Console.Write("Ваш выбор / Your choice: ");
        
        string langChoice = Console.ReadLine();
        
        if (langChoice == "2")
        {
            SetEnglishLanguage();
            Console.Title = "E621.NET Downloader";
        }
        else
        {
            SetRussianLanguage();
            Console.Title = "E621.NET Загрузчик";
        }

        string logFileName = $"e621_download_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        logWriter = new StreamWriter(logFileName, true);
        
        Console.WriteLine($"\n{lang["title"]}\n");
        LogMessage($"{lang["log_start"]}: {DateTime.Now}");

        client.DefaultRequestHeaders.Add("User-Agent", "MyDownloaderApp/1.0 (by YourUsername)");

        try
        {
            Console.WriteLine(lang["what_to_download"]);
            Console.WriteLine($"1. {lang["option_posts"]}");
            Console.WriteLine($"2. {lang["option_pools"]}");
            Console.WriteLine($"3. {lang["option_favorites"]}");
            Console.Write($"{lang["your_choice"]} (1, 2 {lang["or"]} 3): ");

            string downloadTypeChoice = Console.ReadLine();
            string searchQuery = "";
            string downloadTypeName = "";
            string folderIdentifier = "";

            switch (downloadTypeChoice)
            {
                case "1":
                    Console.Write($"\n{lang["enter_tags"]}: ");
                    searchQuery = Console.ReadLine();
                    downloadTypeName = "posts";
                    folderIdentifier = CleanFileName(searchQuery.Replace(" ", "_"));
                    LogMessage($"{lang["log_posts"]}: {searchQuery}");
                    break;

                case "2":
                    Console.Write($"\n{lang["enter_pool_url"]}: ");
                    string poolUrl = Console.ReadLine();

                    Match poolMatch = Regex.Match(poolUrl, @"/pools/(\d+)");
                    if (poolMatch.Success)
                    {
                        string poolId = poolMatch.Groups[1].Value;
                        searchQuery = $"pool:{poolId}";
                        downloadTypeName = "pool";
                        folderIdentifier = $"pool_{poolId}";
                        LogMessage($"{lang["log_pool"]}: {poolUrl}");
                    }
                    else
                    {
                        Console.WriteLine(lang["invalid_pool_url"]);
                        LogMessage($"{lang["log_error_pool"]}: {poolUrl}");
                        Console.ReadLine();
                        return;
                    }
                    break;

                case "3":
                    Console.Write($"\n{lang["enter_username"]}: ");
                    string username = Console.ReadLine();
                    searchQuery = $"fav:{username}";
                    downloadTypeName = "fav";
                    folderIdentifier = CleanFileName(username);
                    LogMessage($"{lang["log_fav"]}: {username}");
                    break;

                default:
                    Console.WriteLine(lang["invalid_choice"]);
                    LogMessage($"{lang["log_error_choice"]}: {downloadTypeChoice}");
                    Console.ReadLine();
                    return;
            }

            if (string.IsNullOrWhiteSpace(searchQuery))
            {
                Console.WriteLine(lang["empty_query"]);
                LogMessage(lang["log_empty_query"]);
                Console.ReadLine();
                return;
            }

            Console.WriteLine($"\n{lang["select_content"]}:");
            Console.WriteLine($"1. {lang["content_images"]}");
            Console.WriteLine($"2. {lang["content_videos"]}");
            Console.WriteLine($"3. {lang["content_all"]}");
            Console.Write($"{lang["your_choice"]} (1, 2 {lang["or"]} 3): ");

            string contentTypeChoice = Console.ReadLine();
            string contentType = "image";
            string folderTypeName = lang["type_images"];

            if (contentTypeChoice == "1")
            {
                contentType = "image";
                folderTypeName = lang["type_images"];
                Console.WriteLine(lang["selected_images"]);
                LogMessage(lang["log_images"]);
            }
            else if (contentTypeChoice == "2")
            {
                contentType = "video";
                folderTypeName = lang["type_videos"];
                Console.WriteLine(lang["selected_videos"]);
                LogMessage(lang["log_videos"]);
            }
            else if (contentTypeChoice == "3")
            {
                contentType = "all";
                folderTypeName = lang["type_all"];
                Console.WriteLine(lang["selected_all"]);
                LogMessage(lang["log_all"]);
            }
            else
            {
                Console.WriteLine($"{lang["default_choice"]} {lang["type_images"]}");
                LogMessage(lang["log_default_images"]);
            }

            Console.Write($"\n{lang["how_many_posts"]}: ");
            string limitInput = Console.ReadLine();
            int limit = 50;

            if (int.TryParse(limitInput, out int parsedLimit) && parsedLimit > 0 && parsedLimit <= 320)
            {
                limit = parsedLimit;
            }
            else
            {
                Console.WriteLine($"{lang["using_default"]} {limit}");
            }

            LogMessage($"{lang["log_limit"]}: {limit}");

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
            Console.WriteLine($"\n{lang["folder_created"]}: {downloadFolder}");
            LogMessage($"{lang["log_folder"]}: {downloadFolder}");

            CreateFolderInfoFile(downloadFolder, searchQuery, contentType, limit);

            Console.WriteLine($"\n{lang["searching_content"]}: {searchQuery}...");
            LogMessage($"{lang["log_searching"]}: {searchQuery}");

            var posts = await GetPosts(searchQuery, limit);

            if (posts == null || posts.Count == 0)
            {
                Console.WriteLine(lang["no_posts_found"]);
                LogMessage(lang["log_no_posts"]);
                Console.ReadLine();
                return;
            }

            Console.WriteLine($"{lang["posts_found"]}: {posts.Count}");
            LogMessage($"{lang["log_found"]}: {posts.Count}");

            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine(lang["download_status"]);
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
                    LogMessage($"{lang["log_skipped"]} {post.Id}: {lang["log_wrong_type"]}");
                    continue;
                }

                string fileName = $"{post.Id}_{Path.GetFileName(new Uri(fileUrl).LocalPath)}";
                string filePath = Path.Combine(downloadFolder, fileName);

                if (File.Exists(filePath))
                {
                    Console.WriteLine($"[{i + 1}/{posts.Count}] ✓ {lang["already_exists"]}: {fileName}");
                    LogMessage($"[{lang["log_exists"]}] {fileName}");
                    skippedCount++;
                    continue;
                }

                Console.Write($"[{i + 1}/{posts.Count}] {lang["downloading"]}: {fileName}... ");

                if (await DownloadFile(fileUrl, filePath))
                {
                    Console.WriteLine("✓ " + lang["success"]);
                    LogMessage($"[{lang["log_success"]}] {fileName}");
                    downloadedCount++;
                }
                else
                {
                    Console.WriteLine("✗ " + lang["error"]);
                    LogMessage($"[{lang["log_error"]}] {fileName}");
                    failedCount++;
                }

                await Task.Delay(1000);
            }

            Console.WriteLine("\n" + new string('=', 50));
            Console.WriteLine(lang["results"]);
            Console.WriteLine(new string('=', 50));
            Console.WriteLine($"✓ {lang["downloaded"]}: {downloadedCount} {lang["files"]}");
            Console.WriteLine($"↷ {lang["skipped"]}: {skippedCount} {lang["files"]}");
            Console.WriteLine($"✗ {lang["failed"]}: {failedCount} {lang["files"]}");
            Console.WriteLine($"📁 {lang["folder"]}: {downloadFolder}");
            Console.WriteLine($"📋 {lang["log_file"]}: {logFileName}");
            Console.WriteLine(new string('=', 50));

            LogMessage($"=== {lang["log_results"]} ===");
            LogMessage($"{lang["success"]}: {downloadedCount}");
            LogMessage($"{lang["skipped"]}: {skippedCount}");
            LogMessage($"{lang["failed"]}: {failedCount}");
            LogMessage($"{lang["folder"]}: {downloadFolder}");
            LogMessage($"=== {lang["log_completed"]}: {DateTime.Now} ===");

            if (downloadedCount > 0)
            {
                Console.Write($"\n{lang["open_folder"]}? (y/n): ");
                string openFolder = Console.ReadLine();
                if (openFolder != null && openFolder.ToLower() == "y")
                {
                    System.Diagnostics.Process.Start("explorer.exe", downloadFolder);
                    LogMessage(lang["log_folder_opened"]);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n✗ {lang["critical_error"]}: {ex.Message}");
            LogMessage($"{lang["log_critical"]}: {ex.Message}");
        }
        finally
        {
            logWriter?.Close();
        }

        Console.WriteLine($"\n{lang["press_enter"]}...");
        Console.ReadLine();
    }

    static void SetRussianLanguage()
    {
        lang["title"] = "=== E621.NET Загрузчик ===";
        lang["what_to_download"] = "Что вы хотите скачать?";
        lang["option_posts"] = "Посты (по тегам)";
        lang["option_pools"] = "Пулы (коллекции)";
        lang["option_favorites"] = "Избранное пользователей";
        lang["your_choice"] = "Ваш выбор";
        lang["or"] = "или";
        lang["enter_tags"] = "Введите теги для поиска";
        lang["enter_pool_url"] = "Введите ссылку на пул";
        lang["enter_username"] = "Введите имя пользователя";
        lang["invalid_pool_url"] = "Неверная ссылка на пул!";
        lang["invalid_choice"] = "Неверный выбор!";
        lang["empty_query"] = "Ошибка: запрос пустой!";
        lang["select_content"] = "Выберите тип контента";
        lang["content_images"] = "Изображения (jpg, png, gif)";
        lang["content_videos"] = "Видео (webm, mp4)";
        lang["content_all"] = "Всё (изображения и видео)";
        lang["type_images"] = "изображения";
        lang["type_videos"] = "видео";
        lang["type_all"] = "все";
        lang["selected_images"] = "Выбраны изображения";
        lang["selected_videos"] = "Выбраны видео";
        lang["selected_all"] = "Выбрано всё";
        lang["default_choice"] = "Неверный выбор, использую по умолчанию";
        lang["how_many_posts"] = "Сколько постов скачать? (макс 320, реком. 50)";
        lang["using_default"] = "Использую значение по умолчанию";
        lang["folder_created"] = "Папка создана";
        lang["searching_content"] = "Ищу контент";
        lang["no_posts_found"] = "Посты не найдены!";
        lang["posts_found"] = "Найдено постов";
        lang["download_status"] = "СТАТУС СКАЧИВАНИЯ";
        lang["already_exists"] = "Уже существует";
        lang["downloading"] = "Скачиваю";
        lang["success"] = "УСПЕХ";
        lang["error"] = "ОШИБКА";
        lang["results"] = "ИТОГИ СКАЧИВАНИЯ";
        lang["downloaded"] = "Успешно скачано";
        lang["skipped"] = "Пропущено";
        lang["failed"] = "Ошибок";
        lang["files"] = "файлов";
        lang["folder"] = "Папка";
        lang["log_file"] = "Лог файл";
        lang["open_folder"] = "Открыть папку с файлами";
        lang["critical_error"] = "КРИТИЧЕСКАЯ ОШИБКА";
        lang["press_enter"] = "Нажмите Enter для выхода";
        
        lang["log_start"] = "=== Запуск программы";
        lang["log_posts"] = "Выбраны Posts по тегам";
        lang["log_pool"] = "Выбран Pool";
        lang["log_fav"] = "Выбраны избранные пользователя";
        lang["log_error_pool"] = "Ошибка: неверная ссылка на pool";
        lang["log_error_choice"] = "Неверный выбор типа скачивания";
        lang["log_empty_query"] = "Ошибка: пустой поисковый запрос";
        lang["log_images"] = "Выбран тип: изображения";
        lang["log_videos"] = "Выбран тип: видео";
        lang["log_all"] = "Выбран тип: всё";
        lang["log_default_images"] = "Неверный выбор типа контента, используется изображения по умолчанию";
        lang["log_limit"] = "Лимит скачивания";
        lang["log_folder"] = "Папка для скачивания";
        lang["log_searching"] = "Поиск контента";
        lang["log_no_posts"] = "Посты не найдены";
        lang["log_found"] = "Найдено постов";
        lang["log_skipped"] = "Пропущено Пост";
        lang["log_wrong_type"] = "не подходит под выбранный тип";
        lang["log_exists"] = "УЖЕ ЕСТЬ";
        lang["log_success"] = "УСПЕХ";
        lang["log_error"] = "ОШИБКА";
        lang["log_results"] = "ИТОГИ";
        lang["log_completed"] = "Завершено";
        lang["log_folder_opened"] = "Открыта папка с файлами";
        lang["log_critical"] = "КРИТИЧЕСКАЯ ОШИБКА";
        lang["log_error_info"] = "Ошибка создания файла информации";
        lang["log_api_request"] = "Запрос к API";
        lang["log_api_response"] = "Получен ответ от API, размер";
        lang["log_error_posts"] = "Ошибка получения постов";
        lang["log_download_error"] = "Ошибка загрузки файла";
        lang["log_info_file"] = "Создан файл информации";
    }

    static void SetEnglishLanguage()
    {
        lang["title"] = "=== E621.NET Downloader ===";
        lang["what_to_download"] = "What do you want to download?";
        lang["option_posts"] = "Posts (by tags)";
        lang["option_pools"] = "Pools (collections)";
        lang["option_favorites"] = "User favorites";
        lang["your_choice"] = "Your choice";
        lang["or"] = "or";
        lang["enter_tags"] = "Enter tags for search";
        lang["enter_pool_url"] = "Enter pool URL";
        lang["enter_username"] = "Enter username";
        lang["invalid_pool_url"] = "Invalid pool URL!";
        lang["invalid_choice"] = "Invalid choice!";
        lang["empty_query"] = "Error: query is empty!";
        lang["select_content"] = "Select content type";
        lang["content_images"] = "Images (jpg, png, gif)";
        lang["content_videos"] = "Videos (webm, mp4)";
        lang["content_all"] = "All (images and videos)";
        lang["type_images"] = "images";
        lang["type_videos"] = "videos";
        lang["type_all"] = "all";
        lang["selected_images"] = "Selected images";
        lang["selected_videos"] = "Selected videos";
        lang["selected_all"] = "Selected all";
        lang["default_choice"] = "Invalid choice, using default";
        lang["how_many_posts"] = "How many posts to download? (max 320, rec. 50)";
        lang["using_default"] = "Using default value";
        lang["folder_created"] = "Folder created";
        lang["searching_content"] = "Searching content";
        lang["no_posts_found"] = "No posts found!";
        lang["posts_found"] = "Posts found";
        lang["download_status"] = "DOWNLOAD STATUS";
        lang["already_exists"] = "Already exists";
        lang["downloading"] = "Downloading";
        lang["success"] = "SUCCESS";
        lang["error"] = "ERROR";
        lang["results"] = "DOWNLOAD RESULTS";
        lang["downloaded"] = "Successfully downloaded";
        lang["skipped"] = "Skipped";
        lang["failed"] = "Failed";
        lang["files"] = "files";
        lang["folder"] = "Folder";
        lang["log_file"] = "Log file";
        lang["open_folder"] = "Open folder with files";
        lang["critical_error"] = "CRITICAL ERROR";
        lang["press_enter"] = "Press Enter to exit";

        lang["log_start"] = "=== Program start";
        lang["log_posts"] = "Selected Posts by tags";
        lang["log_pool"] = "Selected Pool";
        lang["log_fav"] = "Selected user favorites";
        lang["log_error_pool"] = "Error: invalid pool URL";
        lang["log_error_choice"] = "Invalid download type choice";
        lang["log_empty_query"] = "Error: empty search query";
        lang["log_images"] = "Selected type: images";
        lang["log_videos"] = "Selected type: videos";
        lang["log_all"] = "Selected type: all";
        lang["log_default_images"] = "Invalid content type choice, using default images";
        lang["log_limit"] = "Download limit";
        lang["log_folder"] = "Download folder";
        lang["log_searching"] = "Searching content";
        lang["log_no_posts"] = "No posts found";
        lang["log_found"] = "Posts found";
        lang["log_skipped"] = "SKIPPED Post";
        lang["log_wrong_type"] = "doesn't match selected type";
        lang["log_exists"] = "ALREADY EXISTS";
        lang["log_success"] = "SUCCESS";
        lang["log_error"] = "ERROR";
        lang["log_results"] = "RESULTS";
        lang["log_completed"] = "Completed";
        lang["log_folder_opened"] = "Folder opened";
        lang["log_critical"] = "CRITICAL ERROR";
        lang["log_error_info"] = "Error creating info file";
        lang["log_api_request"] = "API request";
        lang["log_api_response"] = "API response received, size";
        lang["log_error_posts"] = "Error getting posts";
        lang["log_download_error"] = "Error downloading file";
        lang["log_info_file"] = "Info file created";
    }

    static void CreateFolderInfoFile(string folderPath, string searchQuery, string contentType, int limit)
    {
        try
        {
            string infoFilePath = Path.Combine(folderPath, "folder_info.txt");
            using (StreamWriter infoWriter = new StreamWriter(infoFilePath))
            {
                infoWriter.WriteLine(@"
                                         88                                        88           88                 88                                                       88  88  88                       88  88  
                                         88                                        ""    ,d     88                 88                                                       ""  88  88                       88  88  
                                         88                                              88     88                 88                                                           88  88                       88  88  
88,dPYba,,adPYba,   ,adPPYYba,   ,adPPYb,88   ,adPPYba,        8b      db      d8  88  MM88MMM  88,dPPYba,         88   ,adPPYba,   8b       d8   ,adPPYba,                 88  88  88,dPPYba,    ,adPPYba,  88  88  
88P'   ""88""    ""8a  """"     `Y8  a8""    `Y88  a8P_____88        `8b    d88b    d8'  88    88     88P'    ""8a        88  a8""     ""8a  `8b     d8'  a8P_____88                 88  88  88P'    ""8a  a8P_____88  88  88  
88      88      88  ,adPPPPP88  8b       88  8PP""""""""""""         `8b  d8'`8b  d8'   88    88     88       88        88  8b       d8   `8b   d8'   8PP""""""""""""      aaa        88  88  88       88  8PP""""""""""""  88  88  
88      88      88  88,    ,88  ""8a,   ,d88  ""8b,   ,aa          `8bd8'  `8bd8'    88    88,    88       88        88  ""8a,   ,a8""    `8b,d8'    ""8b,   ,aa      ""88        88  88  88       88  ""8b,   ,aa  88  88  
88      88      88  `""8bbdP""Y8   `""8bbdP""Y8   `""Ybbd8""'            YP      YP      88    ""Y888  88       88        88   `""YbbdP""'       ""8""       `""Ybbd8""'      d8'        88  88  88       88   `""Ybbd8""'  88  88  
                                                                                                                                                                8""");

                infoWriter.WriteLine("\n" + new string('=', 70));
                infoWriter.WriteLine("DOWNLOAD INFORMATION");
                infoWriter.WriteLine(new string('=', 70));
                infoWriter.WriteLine($"Created: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
                infoWriter.WriteLine($"Folder: {Path.GetFileName(folderPath)}");
                infoWriter.WriteLine($"Query: {searchQuery}");
                infoWriter.WriteLine($"Content type: {contentType}");
                infoWriter.WriteLine($"Limit: {limit}");
                infoWriter.WriteLine($"Full path: {folderPath}");
                infoWriter.WriteLine(new string('=', 70));
                infoWriter.WriteLine("Created with E621.NET Downloader");
            }
            LogMessage($"{lang["log_info_file"]}");
        }
        catch (Exception ex)
        {
            LogMessage($"{lang["log_error_info"]}: {ex.Message}");
        }
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
            LogMessage($"{lang["log_api_request"]}: {url}");

            HttpResponseMessage response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            LogMessage($"{lang["log_api_response"]}: {json.Length} bytes");

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
            Console.WriteLine($"{lang["log_error_posts"]}: {ex.Message}");
            LogMessage($"{lang["log_error_posts"]}: {ex.Message}");
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
            LogMessage($"{lang["log_download_error"]} {url}: {ex.Message}");
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
