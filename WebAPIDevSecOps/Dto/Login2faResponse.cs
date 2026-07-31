namespace WebAPIDevSecOps.Dto
{
    public class Login2faResponse
    {
        public string? Token { get; set; }
        public string? RefreshToken { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool Requires2fa { get; set; }
        public string? TempToken { get; set; }
    }
}
