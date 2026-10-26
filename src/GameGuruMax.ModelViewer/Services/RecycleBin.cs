using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace GameGuruMax.ModelViewer.Services;

public static class RecycleBin
{
    public static void DeleteFile(string path, bool sendToRecycleBin)
    {
        if (!File.Exists(path))
            return;

        if (sendToRecycleBin)
        {
            FileSystem.DeleteFile(
                path,
                UIOption.OnlyErrorDialogs,
                RecycleOption.SendToRecycleBin);
        }
        else
        {
            File.Delete(path);
        }
    }
}
