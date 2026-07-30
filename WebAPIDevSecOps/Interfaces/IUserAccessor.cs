namespace WebAPIDevSecOps.Interfaces;

public interface IUserAccessor
{
    string? GetCurrentUsername();
    bool IsAdmin();
    bool IsAuthenticated();
}
