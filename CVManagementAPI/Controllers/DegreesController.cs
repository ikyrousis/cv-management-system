using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CVManagementAPI.Data;
using CVManagementAPI.Models;
using CVManagementAPI.DTOs;

namespace CVManagementAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DegreesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public DegreesController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<DegreeDto>>> GetDegrees()
        {
            var degrees = await _context.Degrees
                .Include(d => d.Candidates)
                .Select(d => new DegreeDto
                {
                    Id = d.Id,
                    Name = d.Name,
                    CreationTime = d.CreationTime,
                    CandidateCount = d.Candidates.Count
                })
                .ToListAsync();

            return Ok(degrees);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<DegreeDto>> GetDegree(int id)
        {
            var degree = await _context.Degrees
                .Include(d => d.Candidates)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (degree == null)
            {
                return NotFound();
            }

            var degreeDto = new DegreeDto
            {
                Id = degree.Id,
                Name = degree.Name,
                CreationTime = degree.CreationTime,
                CandidateCount = degree.Candidates.Count
            };

            return Ok(degreeDto);
        }

        [HttpPost]
        public async Task<ActionResult<DegreeDto>> CreateDegree(DegreeDto degreeDto)
        {
            if (!TryValidateModel(degreeDto))
            {
                return BadRequest(ModelState);
            }

            var existingDegree = await _context.Degrees
                .FirstOrDefaultAsync(d => d.Name.ToLower() == degreeDto.Name.ToLower());
            
            if (existingDegree != null)
            {
                return BadRequest("A degree with this name already exists");
            }

            var degree = new Degree
            {
                Name = degreeDto.Name,
                CreationTime = DateTime.Now
            };

            _context.Degrees.Add(degree);
            await _context.SaveChangesAsync();

            var responseDto = new DegreeDto
            {
                Id = degree.Id,
                Name = degree.Name,
                CreationTime = degree.CreationTime,
                CandidateCount = 0
            };

            return CreatedAtAction(nameof(GetDegree), new { id = degree.Id }, responseDto);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateDegree(int id, DegreeDto degreeDto)
        {
            var degree = await _context.Degrees.FindAsync(id);
            if (degree == null)
            {
                return NotFound();
            }

            if (!TryValidateModel(degreeDto))
            {
                return BadRequest(ModelState);
            }

            var existingDegree = await _context.Degrees
                .FirstOrDefaultAsync(d => d.Name.ToLower() == degreeDto.Name.ToLower() && d.Id != id);
            
            if (existingDegree != null)
            {
                return BadRequest("A degree with this name already exists");
            }

            degree.Name = degreeDto.Name;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!DegreeExists(id))
                {
                    return NotFound();
                }
                throw;
            }

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteDegree(int id)
        {
            var degree = await _context.Degrees
                .Include(d => d.Candidates)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (degree == null)
            {
                return NotFound();
            }

            if (degree.Candidates.Any())
            {
                return BadRequest("Cannot delete degree that is assigned to candidates");
            }

            _context.Degrees.Remove(degree);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("cleanup")]
        public async Task<IActionResult> CleanupUnusedDegrees()
        {
            var unusedDegrees = await _context.Degrees
                .Include(d => d.Candidates)
                .Where(d => !d.Candidates.Any())
                .ToListAsync();

            if (!unusedDegrees.Any())
            {
                return Ok(new { message = "No unused degrees found", deletedCount = 0 });
            }

            _context.Degrees.RemoveRange(unusedDegrees);
            await _context.SaveChangesAsync();

            return Ok(new { 
                message = $"Successfully deleted {unusedDegrees.Count} unused degrees", 
                deletedCount = unusedDegrees.Count,
                deletedDegrees = unusedDegrees.Select(d => new { d.Id, d.Name }).ToList()
            });
        }

        private bool DegreeExists(int id)
        {
            return _context.Degrees.Any(e => e.Id == id);
        }
    }
}
