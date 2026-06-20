using System.IO;
using System.Text.RegularExpressions;

namespace ClinicScheduler.Web.Services.Skills;

public class SkillMetadata
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Instructions { get; set; } = "";
}

public interface ISkillRegistry
{
    IEnumerable<SkillMetadata> GetAllSkills();
    SkillMetadata? GetSkill(string name);
    string GetSystemPromptCatalog();
}

public class SkillRegistry : ISkillRegistry
{
    private readonly List<SkillMetadata> _skills = new();

    public SkillRegistry(IWebHostEnvironment env)
    {
        var skillsDir = Path.Combine(env.ContentRootPath, "Skills");
        if (Directory.Exists(skillsDir))
        {
            var skillFiles = Directory.GetFiles(skillsDir, "SKILL.md", SearchOption.AllDirectories);
            foreach (var file in skillFiles)
            {
                var skill = ParseSkillFile(file);
                if (skill != null)
                {
                    _skills.Add(skill);
                }
            }
        }
    }

    private SkillMetadata? ParseSkillFile(string filePath)
    {
        var content = File.ReadAllText(filePath);
        var match = Regex.Match(content, @"^---\s*\n(.*?)\n---\s*\n(.*)$", RegexOptions.Singleline);
        if (!match.Success) return null;

        var frontmatter = match.Groups[1].Value;
        var instructions = match.Groups[2].Value.Trim();

        var nameMatch = Regex.Match(frontmatter, @"^name:\s*(.+)$", RegexOptions.Multiline);
        var descMatch = Regex.Match(frontmatter, @"^description:\s*(.+)$", RegexOptions.Multiline);

        if (!nameMatch.Success || !descMatch.Success) return null;

        return new SkillMetadata
        {
            Name = nameMatch.Groups[1].Value.Trim(),
            Description = descMatch.Groups[1].Value.Trim(),
            Instructions = instructions
        };
    }

    public IEnumerable<SkillMetadata> GetAllSkills() => _skills;

    public SkillMetadata? GetSkill(string name) => _skills.FirstOrDefault(s => s.Name == name);

    public string GetSystemPromptCatalog()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are a Clinic Assistant Agent. You help patients and clinic staff manage appointments.");
        sb.AppendLine("You have access to the following tools. Use them directly when needed:");
        foreach (var skill in _skills)
        {
            sb.AppendLine($"- {skill.Name}: {skill.Description}");
            if (!string.IsNullOrWhiteSpace(skill.Instructions))
            {
                sb.AppendLine($"  Instructions: {skill.Instructions}");
            }
        }
        return sb.ToString();
    }
}
