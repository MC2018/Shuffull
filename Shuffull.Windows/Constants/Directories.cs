using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections.Generic;
using System.Text;

namespace Shuffull.Windows.Constants
{
    internal class Directories
    {
        public static readonly string LocalDataAbsolutePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shuffull");
        public static readonly string MusicFolderAbsolutePath = Path.Combine(LocalDataAbsolutePath, "music");
        public static readonly string DatabaseFileAbsolutePath = Path.Combine(LocalDataAbsolutePath, "data.db3");
    }
}
