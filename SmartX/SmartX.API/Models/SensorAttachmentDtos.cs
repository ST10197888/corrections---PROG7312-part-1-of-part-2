namespace SmartX.API.Models
{
    public sealed class SensorAttachmentDto
    {
        public string MacAddress { get; set; } = "";
        public string FileName { get; set; } = "";
        public string ContentType { get; set; } = "application/octet-stream";
        public DateTime UploadedAt { get; set; }
    }

    public sealed class SensorAttachmentDownload
    {
        public byte[] Content { get; set; } = Array.Empty<byte>();
        public string FileName { get; set; } = "";
        public string ContentType { get; set; } = "application/octet-stream";
    }
}