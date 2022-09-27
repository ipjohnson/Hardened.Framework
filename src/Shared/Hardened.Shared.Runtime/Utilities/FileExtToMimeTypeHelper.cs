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
            switch (fileExtension.ToLowerInvariant().TrimStart('.'))
            {
                case "bz2":
                    return ("application/x-bzip2", true);

                case "css":
                    return ("text/css", false);

                case "csv":
                    return ("text/csv", false);

                case "docx":
                    return ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", false);

                case "gz":
                    return ("application/gzip", true);

                case "gif":
                    return ("image/gif",true);

                case "html":
                    return ("text/html", false);

                case "jpeg":
                case "jpg":
                    return ("image/jpeg", true);

                case "pdf":
                    return ("application/pdf", true);

                case "png":
                    return ("image/png", true);

                case "pptx":
                    return ("image/pptx", true);

                case "txt":
                case "text":
                    return ("text/plain", false);

                case "xml":
                    return ("text/xml", false);

                case "xlsx":
                    return ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", true);

                case "zip":
                    return ("application/zip", true);
            }

            return ("application/bin", true);
        }
    }
}
