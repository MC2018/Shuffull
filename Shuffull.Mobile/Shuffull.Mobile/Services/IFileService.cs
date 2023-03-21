using System;
using System.Collections.Generic;
using System.Text;

namespace Shuffull.Mobile.Services
{
    public interface IFileService
    {
        string GetRootPath();
        void WriteFile(object data, string directory);
        T ReadFile<T>(string directory);
        void DeleteFile(string directory);
        string[] GetFiles(string directory);
    }
}
