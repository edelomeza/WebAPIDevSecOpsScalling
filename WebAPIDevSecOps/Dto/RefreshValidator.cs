using FluentValidation;

namespace WebAPIDevSecOps.Dto
{
    public class RefreshValidator : AbstractValidator<RefreshRequest>
    {
        public RefreshValidator()
        {
            RuleFor(x => x.RefreshToken)
                .NotEmpty().WithMessage("El refresh token es requerido.");
        }
    }
}
