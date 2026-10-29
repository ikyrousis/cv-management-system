using System.Linq.Expressions;
using CVManagement.DTOs;
using CVManagement.Entities;

namespace CVManagement.Repositories;

/// <summary>Single definition of entity-to-read-model mapping, used by both read and re-read paths.</summary>
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
        CreationTime = c.CreationTime
        // FullName is filled after materialization; SQL concatenation would join ciphertexts.
    };

    public static readonly Expression<Func<Degree, DegreeDto>> DegreeToDto = d => new DegreeDto
    {
        Id = d.Id,
        Name = d.Name,
        CreationTime = d.CreationTime,
        CandidateCount = d.Candidates.Count
    };
}
