using CVManagement.Abstractions;
using CVManagement.DTOs;
using CVManagement.Entities;
using CVManagement.Results;
using CVManagement.Services;
using NSubstitute;
using Xunit;

namespace CVManagement.Tests.Unit;

public sealed class DegreeServiceTests
{
    private readonly IDegreeRepository _degrees = Substitute.For<IDegreeRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private DegreeService CreateSut() => new(_degrees, _unitOfWork);

    [Fact]
    public async Task CreateAsync_DuplicateName_ReturnsValidationFailure()
    {
        _degrees.NameTakenAsync("BSc", Arg.Is<int?>(x => x == null), Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateSut().CreateAsync(new DegreeRequest { Name = "BSc" });

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Validation, result.Failure.Kind);
        _degrees.DidNotReceive().Add(Arg.Any<Degree>());
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync();
    }

    [Fact]
    public async Task CreateAsync_FreshName_SavesAndReturnsPersistedDto()
    {
        var stored = new DegreeDto { Id = 10, Name = "MSc" };
        _degrees.GetAsync(Arg.Any<int>()).Returns(stored);

        var result = await CreateSut().CreateAsync(new DegreeRequest { Name = "MSc" });

        Assert.True(result.IsSuccess);
        Assert.Same(stored, result.Value);
        _degrees.Received(1).Add(Arg.Is<Degree>(d => d.Name == "MSc"));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_NameTakenByAnotherDegree_ReturnsValidationFailure()
    {
        _degrees.FindAsync(1).Returns(new Degree { Id = 1, Name = "BSc" });
        _degrees.NameTakenAsync("MSc", Arg.Is<int?>(1), Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateSut().UpdateAsync(1, new DegreeRequest { Name = "MSc" });

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Validation, result.Failure.Kind);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync();
    }

    [Fact]
    public async Task UpdateAsync_HappyPath_RenamesAndSaves()
    {
        var degree = new Degree { Id = 1, Name = "BSc" };
        _degrees.FindAsync(1).Returns(degree);
        _degrees.NameTakenAsync("MSc", Arg.Is<int?>(x => x == 1), Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateSut().UpdateAsync(1, new DegreeRequest { Name = "MSc" });

        Assert.True(result.IsSuccess);
        Assert.Equal("MSc", degree.Name);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_DegreeAssignedToCandidates_IsBlocked()
    {
        var degree = new Degree { Id = 1, Name = "BSc" };
        _degrees.FindAsync(1).Returns(degree);
        _degrees.IsAssignedToCandidatesAsync(1).Returns(true);

        var result = await CreateSut().DeleteAsync(1);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Validation, result.Failure.Kind);
        _degrees.DidNotReceive().Remove(Arg.Any<Degree>());
    }

    [Fact]
    public async Task DeleteAsync_UnassignedDegree_RemovesAndSaves()
    {
        var degree = new Degree { Id = 1, Name = "BSc" };
        _degrees.FindAsync(1).Returns(degree);
        _degrees.IsAssignedToCandidatesAsync(1).Returns(false);

        var result = await CreateSut().DeleteAsync(1);

        Assert.True(result.IsSuccess);
        _degrees.Received(1).Remove(degree);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CleanupUnassignedAsync_NothingToRemove_ReportsZero()
    {
        _degrees.GetUnassignedAsync().Returns(new List<Degree>());

        var result = await CreateSut().CleanupUnassignedAsync();

        Assert.Equal(0, result.DeletedCount);
        _degrees.DidNotReceiveWithAnyArgs().RemoveRange(Arg.Any<IEnumerable<Degree>>());
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync();
    }

    [Fact]
    public async Task CleanupUnassignedAsync_RemovesAllUnassignedAndReportsThem()
    {
        var unassigned = new List<Degree>
        {
            new() { Id = 2, Name = "MSc" },
            new() { Id = 3, Name = "PhD" }
        };
        _degrees.GetUnassignedAsync().Returns(unassigned);

        var result = await CreateSut().CleanupUnassignedAsync();

        Assert.Equal(2, result.DeletedCount);
        Assert.Equal(new[] { 2, 3 }, result.DeletedDegrees.Select(d => d.Id));
        _degrees.Received(1).RemoveRange(unassigned);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAllAsync_PassesPagingThroughAndReportsTotal()
    {
        var items = new List<DegreeDto> { new() { Id = 1, Name = "BSc" } };
        _degrees.GetAllAsync(2, 5, Arg.Any<CancellationToken>()).Returns(items);
        _degrees.CountAsync(Arg.Any<CancellationToken>()).Returns(12);

        var result = await CreateSut().GetAllAsync(2, 5);

        Assert.Same(items, result.Items);
        Assert.Equal(12, result.TotalCount);
    }

    [Fact]
    public async Task CreateAsync_UniqueConstraintRaceAtSave_ReturnsValidationFailure()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new UniqueConstraintViolationException("dup", new Exception())));

        var result = await CreateSut().CreateAsync(new DegreeRequest { Name = "BSc" });

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Validation, result.Failure.Kind);
    }

    [Fact]
    public async Task UpdateAsync_UniqueConstraintRaceAtSave_ReturnsValidationFailure()
    {
        _degrees.FindAsync(1).Returns(new Degree { Id = 1, Name = "BSc" });
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new UniqueConstraintViolationException("dup", new Exception())));

        var result = await CreateSut().UpdateAsync(1, new DegreeRequest { Name = "MSc" });

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Validation, result.Failure.Kind);
    }

    [Fact]
    public async Task GetAsync_MissingDegree_ReturnsNotFound()
    {
        _degrees.GetAsync(5).Returns((DegreeDto?)null);

        var result = await CreateSut().GetAsync(5);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.NotFound, result.Failure.Kind);
    }
}
