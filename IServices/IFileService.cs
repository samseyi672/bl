using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Retailbanking.Common.CustomObj;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Retailbanking.BL.IServices
{
    public interface IFileService
    {
        Task<string> SaveFileAsyncForProfilePicture(IFormFile file);
        Task<string> SaveFileAsync(IFormFile file,bool Active);
        Task<string> SaveAdvertFileAsync(IFormFile file, bool Active);
        Task<string> SaveFileAsync(IFormFile file);
        Task<string>  SaveFileAsyncByFilePath(IFormFile file, string filepath);
        Task<string> SaveFileAsync(IFormFile file,string path);
        Task<IEnumerable<AdvertImageOrPictures>> GetAdvertImageOrPictures(string baseUrl,int page, int size);
        Task<IEnumerable<string>> SaveFilesAsync(IEnumerable<IFormFile> files);
        Task<bool> DeleteFilesAsync(List<string> imageNames);
        Task<bool> DeleteAdvertFilesAsync(List<string> imageNames);
        Task<string> GetFilePath(string fileName);
      //  Task<FileStreamResult> BrowserView2(string fileName);
        Task<IEnumerable<string>> GetImageUrls(string baseUrl);
        Task<IEnumerable<string>> GetDownloadIndenityForm(string username, string baseUrl,string Session,int channelId);
    }
}

