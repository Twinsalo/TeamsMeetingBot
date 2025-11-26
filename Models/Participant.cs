namespace TeamsMeetingBot.Models;

public class Participant
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTimeOffset JoinTime { get; set; }
}
