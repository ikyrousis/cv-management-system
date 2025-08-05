using System.ComponentModel.DataAnnotations;

namespace CVManagementAPI.DTOs
{
    public class DegreeDto
    {
        public int Id { get; set; }
        
        [Required(ErrorMessage = "Name is required")]
        public string Name { get; set; } = string.Empty;
        
        public DateTime CreationTime { get; set; }
        public int CandidateCount { get; set; }
    }
}
