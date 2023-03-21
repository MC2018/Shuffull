using Android.App;
using Shuffull.Mobile.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

namespace Shuffull.Mobile.Droid.Services
{
    public class FileService : IFileService
    {
        public string GetRootPath()
        {
            return Application.Context.GetExternalFilesDir(null).ToString();
        }

        private string ValidateDirectory(string directory)
        {
            if (!directory.StartsWith(GetRootPath()))
            {
                directory = Path.Combine(GetRootPath(), directory);
            }

            return directory;
        }

        public void WriteFile(object data, string directory)
        {
            var destination = ValidateDirectory(directory);
            var parent = Directory.GetParent(destination).FullName;
            Directory.CreateDirectory(parent);

            var formatter = new BinaryFormatter();
            using var stream = new MemoryStream();
            formatter.Serialize(stream, data);
            var byteArray = stream.ToArray();
            File.WriteAllBytes(destination, byteArray);
        }

        public T ReadFile<T>(string directory)
        {
            var source = ValidateDirectory(directory);

            if (!File.Exists(source))
            {
                throw new FileNotFoundException($"File not found at {source}");
            }

            var formatter = new BinaryFormatter();
            using var stream = new MemoryStream(File.ReadAllBytes(source));
            return (T)formatter.Deserialize(stream);
        }

        public void DeleteFile(string directory)
        {
            var destination = ValidateDirectory(directory);

            if (File.Exists(destination))
            {
                File.Delete(destination);
            }
        }

        public string[] GetFiles(string directory)
        {
            var destination = ValidateDirectory(directory);
            Directory.CreateDirectory(destination);

            return Directory.GetFiles(destination);
        }
    }
}
