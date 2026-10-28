using CVManagement.Application.Abstractions;
using CVManagement.Application.Dtos;
using CVManagement.Application.Entities;
using CVManagement.Application.Results;
using CVManagement.Application.Services;
using NSubstitute;
using Xunit;

namespace CVManagement.Tests.Unit;

public sealed class CandidateServiceTests
{
    private readonly ICandidateRepository _candidates = Substitute.For<ICandidateRepository>();
    private readonly IDegreeRepository _degrees = Substitute.For<IDegreeRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private CandidateService CreateSut() => new(_candidates, _degrees, _unitOfWork);

    private static CandidateRequest ValidRequest(int? degreeId = null) => new()
    {
        FirstName = "Jane",
        LastName = "Doe",
        Email = "jane@example.com",
        Mobile = "1234567890",
        DegreeId = degreeId
    };

    [Fact]
    public async Task GetAsync_MissingCandidate_ReturnsNotFound()
    {
        _candidates.GetAsync(7).Returns((CandidateDto?)null);

        var result = await CreateSut().GetAsync(7);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.NotFound, result.Failure.Kind);
    }

    [Fact]
    public async Task CreateAsync_UnknownDegree_ReturnsValidationFailure()
    {
        _degrees.ExistsAsync(99).Returns(false);

        var result = await CreateSut().CreateAsync(ValidRequest(degreeId: 99));

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Validation, result.Failure.Kind);
        _candidates.DidNotReceive().Add(Arg.Any<Candidate>());
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync();
    }

    [Fact]
    public async Task CreateAsync_BlankMobile_IsStoredAsNull()
    {
        Candidate? added = null;
        _candidates
            .When(c => c.Add(Arg.Any<Candidate>()))
            .Do(ci => added = ci.Arg<Candidate>());
        var stored = new CandidateDto { Id = 1 };
        _candidates.GetAsync(Arg.Any<int>()).Returns(stored);

        var request = ValidRequest();
        request.Mobile = "   ";

        var result = await CreateSut().CreateAsync(request);

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.Null(added!.Mobile);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateAsync_ReturnsDtoReReadFromRepository()
    {
        var stored = new CandidateDto { Id = 42, FirstName = "Jane" };
        _candidates.GetAsync(Arg.Any<int>()).Returns(stored);

        var result = await CreateSut().CreateAsync(ValidRequest());

        Assert.True(result.IsSuccess);
        Assert.Same(stored, result.Value);
    }

    [Fact]
    public async Task UpdateAsync_MissingCandidate_ReturnsNotFoundWithoutSaving()
    {
        _candidates.FindAsync(5).Returns((Candidate?)null);

        var result = await CreateSut().UpdateAsync(5, ValidRequest());

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.NotFound, result.Failure.Kind);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync();
    }

    [Fact]
    public async Task UploadCvAsync_DisallowedContentType_ReturnsValidationFailure()
    {
        _candidates.FindAsync(1).Returns(new Candidate { Id = 1 });
        var upload = new CvUpload(new byte[] { 1 }, "evil.exe", "application/octet-stream");

        var result = await CreateSut().UploadCvAsync(1, upload);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Validation, result.Failure.Kind);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync();
    }

    [Fact]
    public async Task UploadCvAsync_EmptyFile_ReturnsValidationFailure()
    {
        _candidates.FindAsync(1).Returns(new Candidate { Id = 1 });
        var upload = new CvUpload(Array.Empty<byte>(), "cv.pdf", "application/pdf");

        var result = await CreateSut().UploadCvAsync(1, upload);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Validation, result.Failure.Kind);
    }

    [Fact]
    public async Task UploadCvAsync_Pdf_StoresFileAndSaves()
    {
        var candidate = new Candidate { Id = 1 };
        _candidates.FindAsync(1).Returns(candidate);
        var content = new byte[] { 1, 2, 3 };
        var upload = new CvUpload(content, "cv.pdf", "application/pdf");

        var result = await CreateSut().UploadCvAsync(1, upload);

        Assert.True(result.IsSuccess);
        Assert.Equal(content, candidate.CVFile);
        Assert.Equal("cv.pdf", candidate.CVFileName);
        Assert.Equal("application/pdf", candidate.CVContentType);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCvAsync_MissingCandidate_ReturnsNotFound()
    {
        _candidates.ExistsAsync(3).Returns(false);

        var result = await CreateSut().GetCvAsync(3);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.NotFound, result.Failure.Kind);
    }

    [Fact]
    public async Task GetCvAsync_NoCvStored_ReturnsNotFound()
    {
        _candidates.ExistsAsync(3).Returns(true);
        _candidates.GetCvAsync(3).Returns((CvFileDto?)null);

        var result = await CreateSut().GetCvAsync(3);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.NotFound, result.Failure.Kind);
    }

    [Fact]
    public async Task DeleteCvAsync_ClearsStoredFileFields()
    {
        var candidate = new Candidate
        {
            Id = 1,
            CVFile = new byte[] { 9 },
            CVFileName = "cv.pdf",
            CVContentType = "application/pdf"
        };
        _candidates.FindAsync(1).Returns(candidate);

        var result = await CreateSut().DeleteCvAsync(1);

        Assert.True(result.IsSuccess);
        Assert.Null(candidate.CVFile);
        Assert.Null(candidate.CVFileName);
        Assert.Null(candidate.CVContentType);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_ExistingCandidate_RemovesAndSaves()
    {
        var candidate = new Candidate { Id = 1 };
        _candidates.FindAsync(1).Returns(candidate);

        var result = await CreateSut().DeleteAsync(1);

        Assert.True(result.IsSuccess);
        _candidates.Received(1).Remove(candidate);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
