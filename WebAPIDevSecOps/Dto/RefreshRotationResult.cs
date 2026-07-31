namespace WebAPIDevSecOps.Dto
{
    public class RefreshRotationResult
    {
        public int UsuarioId { get; set; }
        public string NewRefreshToken { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }
}
