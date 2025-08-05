using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CVManagementAPI.Data;
using CVManagementAPI.Models;
using CVManagementAPI.DTOs;
using System.ComponentModel.DataAnnotations;

namespace CVManagementAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CandidatesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public CandidatesController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<CandidateDto>>> GetCandidates()
        {
            var candidates = await _context.Candidates
                .Include(c => c.Degree)
                .Select(c => new CandidateDto
                {
                    Id = c.Id,
                    FirstName = c.FirstName,
                    LastName = c.LastName,
                    Email = c.Email,
                    Mobile = c.Mobile,
                    DegreeId = c.DegreeId,
                    DegreeName = c.Degree != null ? c.Degree.Name : null,
                    CVFileName = c.CVFileName,
                    HasCV = c.CVFile != null,
                    CreationTime = c.CreationTime,
                    FullName = c.FirstName + " " + c.LastName
                })
                .ToListAsync();

            return Ok(candidates);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<CandidateDto>> GetCandidate(int id)
        {
            var candidate = await _context.Candidates
                .Include(c => c.Degree)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (candidate == null)
            {
                return NotFound();
            }

            var candidateDto = new CandidateDto
            {
                Id = candidate.Id,
                FirstName = candidate.FirstName,
                LastName = candidate.LastName,
                Email = candidate.Email,
                Mobile = candidate.Mobile,
                DegreeId = candidate.DegreeId,
                DegreeName = candidate.Degree?.Name,
                CVFileName = candidate.CVFileName,
                HasCV = candidate.CVFile != null,
                CreationTime = candidate.CreationTime,
                FullName = candidate.FirstName + " " + candidate.LastName
            };

            return Ok(candidateDto);
        }

        [HttpPost]
        public async Task<ActionResult<CandidateDto>> CreateCandidate(CandidateDto candidateDto)
        {
            if (!TryValidateModel(candidateDto))
            {
                return BadRequest(ModelState);
            }

            if (candidateDto.DegreeId.HasValue)
            {
                var degreeExists = await _context.Degrees.AnyAsync(d => d.Id == candidateDto.DegreeId.Value);
                if (!degreeExists)
                {
                    return BadRequest("Invalid degree ID");
                }
            }

            var candidate = new Candidate
            {
                FirstName = candidateDto.FirstName,
                LastName = candidateDto.LastName,
                Email = candidateDto.Email,
                Mobile = candidateDto.Mobile,
                DegreeId = candidateDto.DegreeId,
                CreationTime = DateTime.Now
            };

            _context.Candidates.Add(candidate);
            await _context.SaveChangesAsync();

            await _context.Entry(candidate).Reference(c => c.Degree).LoadAsync();

            var responseDto = new CandidateDto
            {
                Id = candidate.Id,
                FirstName = candidate.FirstName,
                LastName = candidate.LastName,
                Email = candidate.Email,
                Mobile = candidate.Mobile,
                DegreeId = candidate.DegreeId,
                DegreeName = candidate.Degree?.Name,
                CVFileName = candidate.CVFileName,
                HasCV = candidate.CVFile != null,
                CreationTime = candidate.CreationTime,
                FullName = candidate.FirstName + " " + candidate.LastName
            };

            return CreatedAtAction(nameof(GetCandidate), new { id = candidate.Id }, responseDto);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCandidate(int id, CandidateDto candidateDto)
        {
            var candidate = await _context.Candidates.FindAsync(id);
            if (candidate == null)
            {
                return NotFound();
            }

            if (!TryValidateModel(candidateDto))
            {
                return BadRequest(ModelState);
            }

            if (candidateDto.DegreeId.HasValue)
            {
                var degreeExists = await _context.Degrees.AnyAsync(d => d.Id == candidateDto.DegreeId.Value);
                if (!degreeExists)
                {
                    return BadRequest("Invalid degree ID");
                }
            }

            candidate.FirstName = candidateDto.FirstName;
            candidate.LastName = candidateDto.LastName;
            candidate.Email = candidateDto.Email;
            candidate.Mobile = candidateDto.Mobile;
            candidate.DegreeId = candidateDto.DegreeId;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!CandidateExists(id))
                {
                    return NotFound();
                }
                throw;
            }

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCandidate(int id)
        {
            var candidate = await _context.Candidates.FindAsync(id);
            if (candidate == null)
            {
                return NotFound();
            }

            _context.Candidates.Remove(candidate);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpPost("{id}/cv")]
        public async Task<IActionResult> UploadCV(int id, IFormFile file)
        {
            var candidate = await _context.Candidates.FindAsync(id);
            if (candidate == null)
            {
                return NotFound();
            }

            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded");
            }

            var allowedTypes = new[] { "application/pdf", "application/msword", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" };
            if (!allowedTypes.Contains(file.ContentType))
            {
                return BadRequest("Only PDF and Word documents are allowed");
            }


            using (var memoryStream = new MemoryStream())
            {
                await file.CopyToAsync(memoryStream);
                candidate.CVFile = memoryStream.ToArray();
                candidate.CVFileName = file.FileName;
                candidate.CVContentType = file.ContentType;
            }

            await _context.SaveChangesAsync();

            return Ok(new { message = "CV uploaded successfully" });
        }

        [HttpGet("{id}/cv")]
        public async Task<IActionResult> DownloadCV(int id)
        {
            var candidate = await _context.Candidates.FindAsync(id);
            if (candidate == null)
            {
                return NotFound();
            }

            if (candidate.CVFile == null)
            {
                return NotFound("No CV found for this candidate");
            }

            return File(candidate.CVFile, candidate.CVContentType ?? "application/octet-stream", candidate.CVFileName);
        }

        [HttpDelete("{id}/cv")]
        public async Task<IActionResult> DeleteCV(int id)
        {
            var candidate = await _context.Candidates.FindAsync(id);
            if (candidate == null)
            {
                return NotFound();
            }

            candidate.CVFile = null;
            candidate.CVFileName = null;
            candidate.CVContentType = null;

            await _context.SaveChangesAsync();

            return NoContent();
        }

        private bool CandidateExists(int id)
        {
            return _context.Candidates.Any(e => e.Id == id);
        }
    }
}
