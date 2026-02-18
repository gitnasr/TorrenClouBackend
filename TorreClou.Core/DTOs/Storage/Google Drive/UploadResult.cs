namespace TorreClou.GoogleDrive.Worker.Services
{


    public sealed class UploadResult { public int TotalFiles; public int FailedFiles; public bool AllFilesUploaded => TotalFiles > 0 && FailedFiles == 0; }


}
