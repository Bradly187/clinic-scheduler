using ClinicScheduler.Web.Services.Skills;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Moq;
using Xunit;

namespace ClinicScheduler.Web.Tests.Unit;

public class SkillRegistryTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _skillsDirectory;
    private readonly Mock<IWebHostEnvironment> _mockEnv;

    public SkillRegistryTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _skillsDirectory = Path.Combine(_tempDirectory, "Skills");
        Directory.CreateDirectory(_skillsDirectory);

        _mockEnv = new Mock<IWebHostEnvironment>();
        _mockEnv.Setup(e => e.ContentRootPath).Returns(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [Fact]
    public void Constructor_ShouldParseValidSkillFile()
    {
        // Arrange
        var skillFolder = Path.Combine(_skillsDirectory, "test_skill");
        Directory.CreateDirectory(skillFolder);
        var skillFilePath = Path.Combine(skillFolder, "SKILL.md");
        
        var skillContent = @"---
name: test_skill
description: A skill for testing purposes.
---
This is the instruction for the test skill.
It can have multiple lines.";
        
        File.WriteAllText(skillFilePath, skillContent);

        // Act
        var registry = new SkillRegistry(_mockEnv.Object);
        var skills = registry.GetAllSkills().ToList();

        // Assert
        skills.Should().HaveCount(1);
        skills[0].Name.Should().Be("test_skill");
        skills[0].Description.Should().Be("A skill for testing purposes.");
        // We use string replace to handle cross-platform line endings issues gracefully
        skills[0].Instructions.Replace("\r\n", "\n").Should().Be("This is the instruction for the test skill.\nIt can have multiple lines.");
    }

    [Fact]
    public void Constructor_ShouldIgnoreInvalidSkillFile()
    {
        // Arrange
        var skillFolder = Path.Combine(_skillsDirectory, "invalid_skill");
        Directory.CreateDirectory(skillFolder);
        var skillFilePath = Path.Combine(skillFolder, "SKILL.md");
        
        var skillContent = @"This file does not have the correct frontmatter format.
name: invalid_skill";
        
        File.WriteAllText(skillFilePath, skillContent);

        // Act
        var registry = new SkillRegistry(_mockEnv.Object);
        var skills = registry.GetAllSkills().ToList();

        // Assert
        skills.Should().BeEmpty();
    }

    [Fact]
    public void GetSkill_ShouldReturnCorrectSkill_WhenSkillExists()
    {
        // Arrange
        var skillFolder = Path.Combine(_skillsDirectory, "test_skill");
        Directory.CreateDirectory(skillFolder);
        File.WriteAllText(Path.Combine(skillFolder, "SKILL.md"), "---\nname: test_skill\ndescription: A test.\n---\nTest instructions.");

        var registry = new SkillRegistry(_mockEnv.Object);

        // Act
        var skill = registry.GetSkill("test_skill");

        // Assert
        skill.Should().NotBeNull();
        skill!.Name.Should().Be("test_skill");
    }

    [Fact]
    public void GetSkill_ShouldReturnNull_WhenSkillDoesNotExist()
    {
        // Arrange
        var registry = new SkillRegistry(_mockEnv.Object);

        // Act
        var skill = registry.GetSkill("nonexistent_skill");

        // Assert
        skill.Should().BeNull();
    }

    [Fact]
    public void GetSystemPromptCatalog_ShouldFormatSkillsCorrectly()
    {
        // Arrange
        var skillFolder1 = Path.Combine(_skillsDirectory, "skill_one");
        Directory.CreateDirectory(skillFolder1);
        File.WriteAllText(Path.Combine(skillFolder1, "SKILL.md"), "---\nname: skill_one\ndescription: The first skill.\n---\nInstruct 1.");

        var skillFolder2 = Path.Combine(_skillsDirectory, "skill_two");
        Directory.CreateDirectory(skillFolder2);
        File.WriteAllText(Path.Combine(skillFolder2, "SKILL.md"), "---\nname: skill_two\ndescription: The second skill.\n---\nInstruct 2.");

        var registry = new SkillRegistry(_mockEnv.Object);

        // Act
        var prompt = registry.GetSystemPromptCatalog();

        // Assert
        prompt.Should().Contain("- skill_one: The first skill.");
        prompt.Should().Contain("- skill_two: The second skill.");
    }
}
