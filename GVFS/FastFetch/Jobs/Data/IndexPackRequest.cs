using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FastFetch.Jobs.Data
{
    public class IndexPackRequest
    {
        public IndexPackRequest(string tempPackFile, BlobDownloadRequest downloadRequest)
        {
            this.TempPackFile = tempPackFile;
            this.DownloadRequest = downloadRequest;
        }

        public BlobDownloadRequest DownloadRequest { get; }

        public string TempPackFile { get; }
    }
}