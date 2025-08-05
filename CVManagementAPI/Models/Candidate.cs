using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CVManagementAPI.Models
{
    public class Candidate
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Last name is required")]
        public string LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "First name is required")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required")]
        [RegularExpression(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$", ErrorMessage = "Please enter a valid email address (emailname@domain.com)")]
        public string Email { get; set; } = string.Empty;

        [RegularExpression(@"^\d{10}$", ErrorMessage = "Mobile must be exactly 10 digits")]
        public string? Mobile { get; set; }

        public int? DegreeId { get; set; }

        public byte[]? CVFile { get; set; }

        public string? CVFileName { get; set; }

        public string? CVContentType { get; set; }

        public DateTime CreationTime { get; set; } = DateTime.Now;

        [ForeignKey("DegreeId")]
        public virtual Degree? Degree { get; set; }

        [NotMapped]
        public string FullName => $"{FirstName} {LastName}";
    }
}
