using System.Linq.Expressions;
using CVManagement.Application.Dtos;
using CVManagement.Application.Entities;

namespace CVManagement.Infrastructure.Persistence;

/// <summary>
/// The only definition of the entity-to-read-model mapping. Read paths project with it, and write
/// paths re-read through it, so a field cannot be mapped one way in one place and another way elsewhere.
/// </summary>
internal static class Projections
{
    public static readonly Expression<Func<Candidate, CandidateDto>> CandidateToDto = c => new CandidateDto
    {
        Id = c.Id,
        LastName = c.LastName,
        FirstName = c.FirstName,
        Email = c.Email,
        Mobile = c.Mobile,
        DegreeId = c.DegreeId,
        DegreeName = c.Degree != null ? c.Degree.Name : null,
        CVFileName = c.CVFileName,
        HasCV = c.CVFile != null,
        CreationTime = c.CreationTime,
        FullName = c.FirstName + " " + c.LastName
    };

    public static readonly Expression<Func<Degree, DegreeDto>> DegreeToDto = d => new DegreeDto
    {
        Id = d.Id,
        Name = d.Name,
        CreationTime = d.CreationTime,
        CandidateCount = d.Candidates.Count
    };
}
