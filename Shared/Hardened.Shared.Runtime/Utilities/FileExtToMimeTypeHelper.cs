using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Runtime.Utilities
{
    public interface IFileExtToMimeTypeHelper
    {
        (string, bool) GetMimeTypeInfo(string fileExtension);
    }

    public class FileExtToMimeTypeHelper : IFileExtToMimeTypeHelper
    {
        public (string, bool) GetMimeTypeInfo(string fileExtension)
        {
            switch (fileExtension.ToLowerInvariant())
            {
                case "css":
                    return ("text/css", false);

                case "csv":
                    return ("text/csv", false);

                case "gif":
                    return ("image/gif",true);

                case "html":
                    return ("text/html",true);

                case "jpeg":
                case "jpg":
                    return ("image/jpeg", true);

                case "pdf":
                    return ("application/pdf", true);

                case "png":
                    return ("image/png", true);

                case "txt":
                case "text":
                    return ("text/plain", false);
            }

            return ("application/" + fileExtension, true);
        }
    }
}
