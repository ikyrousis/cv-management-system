using CVManagement.Abstractions;
using CVManagement.DTOs;
using CVManagement.Entities;
using CVManagement.Results;
using CVManagement.Services;
using CVManagement.Validation;
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
    public async Task GetAllAsync_PassesPagingThroughAndReportsTotal()
    {
        var items = new List<CandidateDto> { new() { Id = 1 } };
        _candidates.GetAllAsync(3, 10, Arg.Any<CancellationToken>()).Returns(items);
        _candidates.CountAsync(Arg.Any<CancellationToken>()).Returns(25);

        var result = await CreateSut().GetAllAsync(3, 10);

        Assert.Same(items, result.Items);
        Assert.Equal(25, result.TotalCount);
    }

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
    public async Task UploadCvAsync_OversizedFile_ReturnsValidationFailureAndSavesNothing()
    {
        _candidates.FindAsync(1).Returns(new Candidate { Id = 1 });
        var upload = new CvUpload(new byte[CvLimits.MaxCvBytes + 1], "big.pdf", "application/pdf");

        var result = await CreateSut().UploadCvAsync(1, upload);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Validation, result.Failure.Kind);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync();
    }

    [Fact]
    public async Task UploadCvAsync_AtTheSizeLimit_IsAccepted()
    {
        var candidate = new Candidate { Id = 1 };
        _candidates.FindAsync(1).Returns(candidate);
        var upload = new CvUpload(new byte[CvLimits.MaxCvBytes], "edge.pdf", "application/pdf");

        var result = await CreateSut().UploadCvAsync(1, upload);

        Assert.True(result.IsSuccess);
        Assert.Equal(CvLimits.MaxCvBytes, candidate.CVFile!.LongLength);
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
    public async Task DeleteCvAsync_ClearsCvWithoutReadingProtectedColumns()
    {
        _candidates.ExistsAsync(1).Returns(true);

        var result = await CreateSut().DeleteCvAsync(1);

        Assert.True(result.IsSuccess);
        _candidates.Received(1).DetachCvById(1);
        _candidates.DidNotReceiveWithAnyArgs().FindAsync(default);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_HappyPath_MutatesTrackedEntityAndSaves()
    {
        var candidate = new Candidate { Id = 1, FirstName = "Old", LastName = "Name", Email = "old@example.com" };
        _candidates.FindAsync(1).Returns(candidate);
        _degrees.ExistsAsync(7).Returns(true);
        var request = new CandidateRequest
        {
            FirstName = "New",
            LastName = "Name",
            Email = "new@example.com",
            Mobile = " 0712345678 ",
            DegreeId = 7
        };

        var result = await CreateSut().UpdateAsync(1, request);

        Assert.True(result.IsSuccess);
        Assert.Equal("New", candidate.FirstName);
        Assert.Equal("new@example.com", candidate.Email);
        Assert.Equal("0712345678", candidate.Mobile);
        Assert.Equal(7, candidate.DegreeId);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllAsync_NoParameters_PassesNullsAndWrapsResult()
    {
        var items = new List<CandidateDto> { new() { Id = 1 } };
        _candidates.GetAllAsync(null, null, Arg.Any<CancellationToken>()).Returns(items);
        _candidates.CountAsync(Arg.Any<CancellationToken>()).Returns(7);

        var result = await CreateSut().GetAllAsync();

        Assert.Same(items, result.Items);
        Assert.Equal(7, result.TotalCount);
    }

    [Fact]
    public async Task UploadCvAsync_WithoutAFileName_ReturnsValidationFailure()
    {
        _candidates.FindAsync(1).Returns(new Candidate { Id = 1 });
        var upload = new CvUpload(new byte[] { 1 }, "   ", "application/pdf");

        var result = await CreateSut().UploadCvAsync(1, upload);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Validation, result.Failure.Kind);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync();
    }

    [Fact]
    public async Task UploadCvAsync_OverlongFileName_ReturnsValidationFailure()
    {
        _candidates.FindAsync(1).Returns(new Candidate { Id = 1 });
        var upload = new CvUpload(new byte[] { 1 }, new string('c', CvLimits.MaxCvFileNameChars + 1) + ".pdf", "application/pdf");

        var result = await CreateSut().UploadCvAsync(1, upload);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Validation, result.Failure.Kind);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync();
    }

    [Fact]
    public async Task DeleteAsync_MissingCandidate_ReturnsNotFound()
    {
        _candidates.ExistsAsync(9).Returns(false);

        var result = await CreateSut().DeleteAsync(9);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.NotFound, result.Failure.Kind);
        _candidates.DidNotReceiveWithAnyArgs().RemoveById(default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync();
    }

    [Fact]
    public async Task UploadCvAsync_MissingCandidate_ReturnsNotFound()
    {
        _candidates.FindAsync(3).Returns((Candidate?)null);

        var result = await CreateSut().UploadCvAsync(3, new CvUpload(new byte[] { 1 }, "cv.pdf", "application/pdf"));

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.NotFound, result.Failure.Kind);
    }

    [Fact]
    public async Task GetCvAsync_HappyPath_ReturnsStoredContent()
    {
        var cv = new CvFileDto { Content = new byte[] { 5 }, FileName = "cv.pdf", ContentType = "application/pdf" };
        _candidates.ExistsAsync(1).Returns(true);
        _candidates.GetCvAsync(1).Returns(cv);

        var result = await CreateSut().GetCvAsync(1);

        Assert.True(result.IsSuccess);
        Assert.Same(cv, result.Value);
    }

    [Fact]
    public async Task DeleteCvAsync_MissingCandidate_ReturnsNotFound()
    {
        _candidates.ExistsAsync(2).Returns(false);

        var result = await CreateSut().DeleteCvAsync(2);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.NotFound, result.Failure.Kind);
        _candidates.DidNotReceiveWithAnyArgs().DetachCvById(default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync();
    }

    [Fact]
    public async Task DeleteAsync_ExistingCandidate_DeletesByKeyAndSaves()
    {
        _candidates.ExistsAsync(1).Returns(true);

        var result = await CreateSut().DeleteAsync(1);

        Assert.True(result.IsSuccess);
        _candidates.Received(1).RemoveById(1);
        _candidates.DidNotReceiveWithAnyArgs().FindAsync(default);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
