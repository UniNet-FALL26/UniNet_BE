using UniNet.Application.DTOs.Projects;
using UniNet.Application.Interfaces;
using UniNet.Application.Services;
using Xunit;

namespace UniNet.Tests;

public sealed class ProjectExtractionValidationTests
{
    private static ProjectDocumentText Document() => new(Guid.NewGuid(), "report.pdf", "pdf", "hash",
        [new(1, "UniNet uses C# and UnknownTool. Nguyễn An is Backend Developer. Started in 2024. https://github.com/team/uninet", PageNumber: 2)]);
    private static SourcedProjectText Text(ProjectDocumentText d, string value, string? quote = null) =>
        new(value, [new(d.FileId, 1, quote ?? d.Segments[0].Text)]);

    [Fact]
    public void ResolvesCatalogOnServerAndLeavesUnknownSkillAndMissingFieldsEmpty()
    {
        var d = Document(); var id = Guid.NewGuid();
        var input = new ProjectInformationExtraction
        {
            Title = Text(d, "UniNet"), StartDate = Text(d, "2024"),
            Skills = [new(Text(d, "C#"), Guid.NewGuid(), "untrusted"), new(Text(d, "UnknownTool"), Guid.NewGuid())],
            Members = [new(Text(d, "Nguyễn An"), Text(d, "Backend Developer"))]
        };
        var result = ProjectExtractionValidator.ValidateAndMatch(input, [d], [new(id, "c#")]);
        Assert.Equal(id, result.Skills[0].SkillId);
        Assert.Equal("c#", result.Skills[0].CatalogName);
        Assert.Null(result.Skills[1].SkillId);
        Assert.Null(result.Description);
        Assert.Null(result.EndDate);
        Assert.Equal("2024", result.StartDate!.Value);
        Assert.Equal(2, d.Segments.Single(s => s.Number == result.Title!.Sources[0].SegmentNumber).PageNumber);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("segment")]
    [InlineData("quote")]
    [InlineData("noSources")]
    public void RejectsFabricatedOrMissingCitations(string kind)
    {
        var d = Document();
        var field = Text(d, "UniNet");
        field = field with { Sources = kind switch
        {
            "file" => [new(Guid.NewGuid(), 1, "UniNet")],
            "segment" => [new(d.FileId, 99, "UniNet")],
            "quote" => [new(d.FileId, 1, "This text does not exist")],
            _ => []
        } };
        var error = Assert.Throws<ProjectExtractionException>(() => ProjectExtractionValidator.ValidateAndMatch(new() { Title = field }, [d], []));
        Assert.Equal("EXTRACTION_INVALID_RESPONSE", error.Code);
    }

    [Fact]
    public void SkillCannotBeInventedEvenWhenCitationItselfExists()
    {
        var d = Document();
        Assert.Throws<ProjectExtractionException>(() => ProjectExtractionValidator.ValidateAndMatch(new()
        { Skills = [new(Text(d, "Python"))] }, [d], []));
    }

    [Fact]
    public void AmbiguousSkillNamesStayUnmatched()
    {
        var d = Document();
        var result = ProjectExtractionValidator.ValidateAndMatch(new() { Skills = [new(Text(d, "C#"))] }, [d],
            [new(Guid.NewGuid(), "C#"), new(Guid.NewGuid(), "c#")]);
        Assert.Null(Assert.Single(result.Skills).SkillId);
    }

    [Theory]
    [InlineData("2024-13")]
    [InlineData("2024-02-30")]
    [InlineData("sometime")]
    public void InvalidDatesAreRejected(string date)
    {
        var d = Document();
        Assert.Throws<ProjectExtractionException>(() => ProjectExtractionValidator.ValidateAndMatch(new() { StartDate = Text(d, date) }, [d], []));
    }

    [Fact]
    public void LinksMustBeHttpAndAppearInQuotedDocumentText()
    {
        var d = Document();
        var result = ProjectExtractionValidator.ValidateAndMatch(new() { RepositoryUrl = Text(d, "https://github.com/team/uninet") }, [d], []);
        Assert.NotNull(result.RepositoryUrl);
        Assert.Throws<ProjectExtractionException>(() => ProjectExtractionValidator.ValidateAndMatch(new()
        { DemoUrl = Text(d, "https://invented.example") }, [d], []));
    }
}
