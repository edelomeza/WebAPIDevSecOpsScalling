namespace WebAPIDevSecOps.Dto
{
    public record Login2faVerifyRequest(string TempToken, string Code);
}
