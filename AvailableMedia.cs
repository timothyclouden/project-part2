using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MediaServer
{
    public class AvailableMedia
    {
        static private string[] fileArray = Array.Empty<string>();
        static string? path;

        public AvailableMedia(string path)
        {
            path = path.ToLower();
            if (AvailableMedia.path == null || !AvailableMedia.path.Equals(path))
            {
                AvailableMedia.path = path;
                fileArray = Directory.GetFiles(path, "*.mp3", SearchOption.AllDirectories)
                    .Union(Directory.GetFiles(path, "*.mp4", SearchOption.AllDirectories))
                    .Union(Directory.GetFiles(path, "*.jpg", SearchOption.AllDirectories))
                    .Union(Directory.GetFiles(path, "*.png", SearchOption.AllDirectories))
                    .Union(Directory.GetFiles(path, "*.gif", SearchOption.AllDirectories))
                    .ToArray();
            }
        }

        public IEnumerable<string> getAvailableFiles()
        {
            return fileArray;
        }

        public string stripPath(string filename)
        {
            return AvailableMedia.path == null 
                ? filename.ToLower() 
                : filename.ToLower().Replace(AvailableMedia.path, "");
        }

        public string getAbsolutePath(int index)
        {
            return fileArray[index];
        }
    }
}

