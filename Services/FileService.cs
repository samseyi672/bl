using Dapper;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Retailbanking.BL.IServices;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.Services
{
    public class FileService : IFileService
    {
        
        private readonly FolderPaths _folderPaths;
        private readonly ILogger<TransferServices> _logger;
        private readonly AppSettings _settings;
        private readonly IGeneric _genServ;
        private readonly DapperContext _context;

        public FileService(IOptions<FolderPaths> folderPaths, ILogger<TransferServices> logger, IOptions<AppSettings> options, IGeneric genServ, DapperContext context)
        {
            _logger = logger;
            _settings = options.Value;
            _genServ = genServ;
            _context = context;
            _folderPaths = folderPaths.Value;
        }

        public Task<string> GetFilePath(string fileName)
        {
            return Task.Run(() =>
            {
                return Path.Combine(_folderPaths.Uploads, fileName);
            });
        }

        public async Task<IEnumerable<string>> GetImageUrls(string baseUrl)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var imageList = await con.QueryAsync<string>("SELECT imagename FROM advertimage WHERE activeimg = true limit 10");
                    var selectedImagePaths = imageList.Select(file => Path.Combine(_folderPaths.AdvertImage, file)).ToList();
                    var imageFiles = selectedImagePaths.Where(file => IsImageFile(file));
                    _logger.LogInformation($"baseUrl {baseUrl} "+ " count "+imageFiles.Count());
                  //  return imageFiles.Select(file => $"{baseUrl}/api/FileService/BrowserView/{Path.GetFileName(file)}");
                   return imageFiles.Select(file => $"{baseUrl}/omnichannel_authentication/api/FileService/BrowserView3/{Path.GetFileName(file)}");//for test
                    //return imageFiles.Select(file => $"{baseUrl}/api/FileService/BrowserView/{Path.GetFileName(file)}"); // for live
                }
            }
            catch (Exception ex)
            {
                // Log the error
                _logger.LogInformation(ex.Message);
                throw;
            }
        }

        // Helper method to check if the file is an image
        private bool IsImageFile(string filePath)
        {
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp" };
            return imageExtensions.Contains(Path.GetExtension(filePath).ToLower());

        }

        public async Task<IEnumerable<string>> GetAdvertImage(string baseUrl)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var imageList = await con.QueryAsync<string>("SELECT imagename FROM advertimage WHERE activeimg = true limit 10");
                    var selectedImagePaths = imageList.Select(file => Path.Combine(_folderPaths.Uploads, file)).ToList();
                    var imageFiles = selectedImagePaths.Where(file => IsImageFile(file));
                    //  return imageFiles.Select(file => $"{baseUrl}/api/FileService/BrowserView/{Path.GetFileName(file)}");
                    return imageFiles.Select(file => $"{baseUrl}/omnichannel_authentication/api/FileService/BrowserView/{Path.GetFileName(file)}");
                }
            }
            catch (Exception ex)
            {
                // Log the error
                _logger.LogInformation(ex.Message);
                throw;
            }
        }

        public async Task<IEnumerable<AdvertImageOrPictures>> GetAdvertImageOrPictures(string baseUrl,int page, int size)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    int skip = (page - 1) * size;
                    int take = size;
                    var imageList = (await con.QueryAsync<AdvertImageOrPictures>($@"SELECT id,activeimg,imagename FROM advertimage limit @Take offset @Skip",new {Take=take,Skip=skip})).ToList();
                    /*
                    imageList.ForEach(x => {
                    x.imagename = $"{baseUrl}/PrimeUser/BrowserView/{Path.GetFileName(x.imagename)}";
                        });
                    */
                    imageList.ForEach(x => {
                        x.imagename = $"{baseUrl}/PrimeUser/AdvertBrowserView/{Path.GetFileName(x.imagename)}";
                    });
                    // var selectedImagePaths = imageList.Select(file => Path.Combine(_folderPaths.Uploads, file)).ToList();
                    // var imageFiles = selectedImagePaths.Where(file => IsImageFile(file));
                    //  return imageFiles.Select(file => $"{baseUrl}/api/FileService/BrowserView/{Path.GetFileName(file)}");
                    // return imageFiles.Select(file => $"{baseUrl}/omnichannel_authentication/api/FileService/BrowserView/{Path.GetFileName(file)}");
                    return imageList;
                }
            }
            catch (Exception ex)
            {
                // Log the error
                _logger.LogInformation(ex.Message);
                throw;
            }
        }

        public async Task<bool> DeleteFilesAsync(List<string> imageNames)
        {
            string uploadPath = _folderPaths.Uploads;
            // Get the full paths of the images to be deleted
            var imagePaths = imageNames.Select(img => Path.Combine(uploadPath, img)).ToList();
            // Iterate over each path and delete the files asynchronously
            foreach (var imagePath in imagePaths)
            {
                if (File.Exists(imagePath))
                {
                    _logger.LogInformation("imagePath "+imagePath);
                    await Task.Run(() => File.Delete(imagePath));
                }
            }
            return true;
        }

        public async Task<bool> DeleteAdvertFilesAsync(List<string> imageNames)
        {
            string uploadPath = _folderPaths.AdvertImage;
            // Get the full paths of the images to be deleted
            var imagePaths = imageNames.Select(img => Path.Combine(uploadPath, img)).ToList();
            // Iterate over each path and delete the files asynchronously
            foreach (var imagePath in imagePaths)
            {
                if (File.Exists(imagePath))
                {
                    _logger.LogInformation("imagePath " + imagePath);
                    await Task.Run(() => File.Delete(imagePath));
                }
            }
            return true;
        }

        public async Task<IEnumerable<string>> SaveFilesAsync(IEnumerable<IFormFile> files)
          {
            var filePaths = new List<string>();
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    foreach (var file in files)
                    {
                        // Validate file
                        if (file == null || file.Length == 0)
                        {
                            throw new ArgumentException("One or more files are not valid.");
                        }
                        // Get unique filename to prevent overwrites
                        string uploadPath = _folderPaths.Uploads;
                        string fileName = Path.GetRandomFileName() + Path.GetExtension(file.FileName);
                        string filePath = Path.Combine(uploadPath, fileName);
                        Console.WriteLine("filePath " + filePath);
                        // Use FileStream with using for proper disposal
                        using (var stream = new FileStream(filePath, FileMode.Create,access: FileAccess.ReadWrite))
                        {
                            await file.CopyToAsync(stream);
                        }

                        // Save filepath to the database using parameterized query to prevent SQL injection
                        await con.ExecuteAsync("INSERT INTO advertimage (imagename, activeimg) VALUES (@imagename, @activeimg)",
                            new { imagename = fileName, activeimg = true });

                        filePaths.Add(filePath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while saving the files.");
                throw; // Rethrow the exception to ensure the caller is aware of the failure
            }
            return filePaths;
        }

        /*
        public Task<PhysicalFile> BrowerView(string filename) {
            var filePath = Path.Combine(_folderPaths.Uploads, filename);

            if (!System.IO.File.Exists(filePath))
            {
                return NotFound();
            }
            //var mimeType = MimeTypes.GetMimeType(filename);
            var mimeType = MimeTypesMap.GetMimeType(filePath);
            return PhysicalFile(filePath, mimeType);
        }
        */

        public async Task<string> SaveFileAsync(IFormFile file)
        {
            try
            {
                // Validate file
                if (file == null || file.Length == 0)
                {
                    throw new ArgumentException("File is not valid.");
                }
                // Get unique filename to prevent overwrites
               // string uploadPath = _folderPaths.Uploads;
                string uploadPath = _folderPaths.AdvertImage;
                string fileName = Path.GetRandomFileName() + Path.GetExtension(file.FileName); // Use GetRandomFileName for uniqueness
                string filePath = Path.Combine(uploadPath, fileName);
                // Use FileStream with using for proper disposal
                _logger.LogInformation("filePath " + filePath);
                using (var stream = new FileStream(filePath, FileMode.Create,access:FileAccess.ReadWrite))
                {
                    await file.CopyToAsync(stream);
                }
                using (IDbConnection con = _context.CreateConnection())
                {
                    await con.ExecuteAsync("INSERT INTO advertimage (imagename, activeimg) VALUES (@imagename, @activeimg)",
                        new { imagename = fileName, activeimg = false });
                }
                _logger.LogInformation("filePath " + filePath);
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while saving the file.");
                throw; // Rethrow the exception to ensure the caller is aware of the failure
            }
        }

        public async Task<string> SaveFileAsyncForProfilePicture(IFormFile file)
        {
            try
            {
                // Validate file
                if (file == null || file.Length == 0)
                {
                    throw new ArgumentException("File is not valid.");
                }
                // Get unique filename to prevent overwrites
                _logger.LogInformation("going to insert file ........");
                string uploadPath = _settings.PicFileUploadPath;
                string fileName = Path.GetRandomFileName() + Path.GetExtension(file.FileName); // Use GetRandomFileName for uniqueness
                string filePath = Path.Combine(uploadPath, fileName);
                // Use FileStream with using for proper disposal
                _logger.LogInformation("filePath " + filePath+ " fileName "+ fileName);
                using (var stream = new FileStream(filePath, FileMode.Create, access: FileAccess.ReadWrite))
                {
                    await file.CopyToAsync(stream);
                }
                _logger.LogInformation("saved filePath " + filePath);
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while saving the file.");
                throw; // Rethrow the exception to ensure the caller is aware of the failure
            }
        }

        public async Task<string> SaveFileAsync(IFormFile file, bool Active)
        {
            try
            {
                // Validate file
                if (file == null || file.Length == 0)
                {
                    throw new ArgumentException("File is not valid.");
                }
                // Get unique filename to prevent overwrites
                string uploadPath = _folderPaths.Uploads;
                string fileName = Path.GetRandomFileName() + Path.GetExtension(file.FileName); // Use GetRandomFileName for uniqueness
                string filePath = Path.Combine(uploadPath, fileName);
                // Use FileStream with using for proper disposal
                _logger.LogInformation("filePath " + filePath);
                using (var stream = new FileStream(filePath, FileMode.Create, access: FileAccess.ReadWrite))
                {
                    await file.CopyToAsync(stream);
                }
                using (IDbConnection con = _context.CreateConnection())
                {
                    await con.ExecuteAsync("INSERT INTO advertimage (imagename, activeimg) VALUES (@imagename, @activeimg)",
                        new { imagename = fileName, activeimg = Active});
                }
                _logger.LogInformation("filePath " + filePath);
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while saving the file.");
                throw; // Rethrow the exception to ensure the caller is aware of the failure
            }
        }


        public async Task<string> SaveAdvertFileAsync(IFormFile file, bool Active)
        {
            try
            {
                // Validate file
                if (file == null || file.Length == 0)
                {
                    throw new ArgumentException("File is not valid.");
                }
                // Get unique filename to prevent overwrites
                string uploadPath = _folderPaths.AdvertImage;
                string fileName = Path.GetRandomFileName() + Path.GetExtension(file.FileName); // Use GetRandomFileName for uniqueness
                string filePath = Path.Combine(uploadPath, fileName);
                // Use FileStream with using for proper disposal
                _logger.LogInformation("filePath " + filePath);
                using (var stream = new FileStream(filePath, FileMode.Create, access: FileAccess.ReadWrite))
                {
                    await file.CopyToAsync(stream);
                }
                using (IDbConnection con = _context.CreateConnection())
                {
                    await con.ExecuteAsync("INSERT INTO advertimage (imagename, activeimg) VALUES (@imagename, @activeimg)",
                        new { imagename = fileName, activeimg = Active });
                }
                _logger.LogInformation("filePath " + filePath);
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while saving the file.");
                throw; // Rethrow the exception to ensure the caller is aware of the failure
            }
        }


        public async Task<string> SaveFileAsyncByFilePath(IFormFile file, string filepath)
        {
            try
            {
                // Validate file
                if (file == null || file.Length == 0)
                {
                    throw new ArgumentException("File is not valid.");
                }
                // Use FileStream with using for proper disposal
                _logger.LogInformation("filePath " + filepath);
                using (var stream = new FileStream(filepath, FileMode.Create, access: FileAccess.ReadWrite))
                {
                    await file.CopyToAsync(stream);
                }
                _logger.LogInformation("filePath " + filepath);
                return filepath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while saving the file.");
                throw; // Rethrow the exception to ensure the caller is aware of the failure
            }
        }
        public async Task<string> SaveFileAsync(IFormFile file, string path)
        {
            try
            {
                // Validate file
                if (file == null || file.Length == 0)
                {
                    throw new ArgumentException("File is not valid.");
                }
                // Get unique filename to prevent overwrites
                string uploadPath = path;
                //string fileName = Path.GetRandomFileName() + Path.GetExtension(file.FileName); // Use GetRandomFileName for uniqueness
                string fileName = file.FileName;
                string filePath = Path.Combine(uploadPath, fileName);
                // Use FileStream with using for proper disposal
                _logger.LogInformation("filePath " + filePath);
                using (var stream = new FileStream(filePath, FileMode.Create, access: FileAccess.ReadWrite))
                {
                    await file.CopyToAsync(stream);
                }
                _logger.LogInformation("filePath " + filePath);
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while saving the file.");
                throw; // Rethrow the exception to ensure the caller is aware of the failure
            }
        }

        public async Task<IEnumerable<string>> GetDownloadIndenityForm(string username, string baseUrl,string Session,int ChannelId)
        {
            try
            {
                using (IDbConnection con = _context.CreateConnection())
                {
                    var validateSession = await _genServ.ValidateSession(username,Session,ChannelId, con);
                    if (!validateSession)
                    {
                        return new List<string>();
                    }
                    _logger.LogInformation("INDEMNITYFORMFORINDIVIDUALACCOUNT " + Path.Combine(_settings.IndemnityformPath, "INDEMNITYFORMFORINDIVIDUALACCOUNT.pdf"));
                    var imageFiles = new List<string>
                    {
                        Path.Combine(_settings.IndemnityformPath, "INDEMNITYFORMFORINDIVIDUALACCOUNT.pdf")
                    };
                    //  return imageFiles.Select(file => $"{baseUrl}/api/FileService/BrowserView/{Path.GetFileName(file)}");
                    Console.WriteLine("file "+imageFiles.ElementAtOrDefault(0)+" filename "+Path.GetFileName(imageFiles.ElementAtOrDefault(0)));
                    return imageFiles.Select(file => $"{baseUrl}/omnichannel_authentication/api/FileService/FileView/{Path.GetFileName(file)}");
                }
            }
            catch (Exception ex)
            {
                // Log the error
                _logger.LogInformation(ex.Message);
                throw;
            }
        }


        /*
       public async Task<string> SaveFileAsync(IFormFile file)
       {
           try
           {
               using (IDbConnection con = _context.CreateConnection())
                   {
                   if (file == null || file.Length == 0)
                   {
                       throw new ArgumentException("File is not valid.");
                   }

                   string uploadPath = _folderPaths.Uploads;
                   string filePath = Path.Combine(uploadPath, file.FileName);

                   using (var stream = new FileStream(filePath, FileMode.OpenOrCreate))
                   {
                       await file.CopyToAsync(stream);
                   }
                   // save the filepath to the db
                  await con.ExecuteAsync("insert into advertimage(imagename,activeimg) values (@imagename,@activeimg)", new { imagename=file.FileName, activeimg =true});
                  return filePath;
               }
           }catch (Exception ex)
           {
               _logger.LogInformation(ex.Message);         
           }
       }
        */

    }
}









































































































