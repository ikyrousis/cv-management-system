using System.ComponentModel.DataAnnotations;

namespace CVManagementAPI.Models
{
    public class Degree
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Name is required")]
        [StringLength(100, ErrorMessage = "Name cannot exceed 100 characters")]
        public string Name { get; set; } = string.Empty;

        public DateTime CreationTime { get; set; } = DateTime.Now;

        public virtual ICollection<Candidate> Candidates { get; set; } = new List<Candidate>();
    }
}
