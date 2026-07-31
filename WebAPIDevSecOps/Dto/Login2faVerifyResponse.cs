namespace WebAPIDevSecOps.Dto
{
    public class Login2faVerifyResponse
    {
        public string Token { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }
}
