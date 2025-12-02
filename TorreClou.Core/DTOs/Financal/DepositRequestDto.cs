using System.ComponentModel.DataAnnotations;

namespace TorreClou.Core.DTOs.Financal
{
    public record DepositRequestDto
    {
        [Required(ErrorMessage = "The amount is required")]
        [Range(10, 100, ErrorMessage = "The amount must be between 10 and 100")]
        public decimal Amount { get; init; }

    }
}