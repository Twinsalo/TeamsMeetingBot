namespace TeamsMeetingBot.Models;

public class SummaryOptions
{
    public string Model { get; set; } = "gpt-4-turbo";
    public int MaxTokens { get; set; } = 8000;
    public double Temperature { get; set; } = 0.7;
    public bool IncludeKeyTopics { get; set; } = true;
    public bool IncludeDecisions { get; set; } = true;
    public bool IncludeActionItems { get; set; } = true;
}
