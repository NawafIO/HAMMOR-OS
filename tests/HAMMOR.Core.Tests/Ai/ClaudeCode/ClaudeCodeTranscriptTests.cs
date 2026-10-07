using HAMMOR.Core.Ai;
using HAMMOR.Infrastructure.Ai.ClaudeCode;
using Xunit;

namespace HAMMOR.Core.Tests.Ai.ClaudeCode;

/// <summary>
/// One print-mode request per turn: history travels on standard input as a
/// labelled transcript, never on the command line.
/// </summary>
public sealed class ClaudeCodeTranscriptTests
{
    [Fact]
    public void A_single_message_goes_as_it_is()
    {
        var prompt = ClaudeCodeTranscript.Compose("Be HAMMOR.", [AiMessage.User("مرحبا")]);

        Assert.Equal("مرحبا", prompt.Prompt);
        Assert.StartsWith("Be HAMMOR.", prompt.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Earlier_turns_become_a_labelled_transcript_oldest_first()
    {
        var prompt = ClaudeCodeTranscript.Compose(
            "Be HAMMOR.",
            [AiMessage.User("First question"), AiMessage.Assistant("First answer"), AiMessage.User("Second question")]);

        Assert.Equal(
            "<conversation>\n<user>\nFirst question\n</user>\n<assistant>\nFirst answer\n</assistant>\n</conversation>\n\nSecond question",
            prompt.Prompt);
    }

    [Fact]
    public void The_oldest_turns_are_dropped_beyond_the_bound()
    {
        var old = new string('a', ClaudeCodeTranscript.MaxPromptChars);
        var prompt = ClaudeCodeTranscript.Compose(
            null,
            [AiMessage.User(old), AiMessage.Assistant("Recent answer"), AiMessage.User("Now")]);

        Assert.DoesNotContain(old, prompt.Prompt, StringComparison.Ordinal);
        Assert.Contains("Recent answer", prompt.Prompt, StringComparison.Ordinal);
        Assert.EndsWith("Now", prompt.Prompt, StringComparison.Ordinal);
        Assert.True(prompt.Prompt.Length <= ClaudeCodeTranscript.MaxPromptChars);
    }

    [Fact]
    public void Without_a_system_prompt_HAMMOR_still_introduces_itself()
    {
        var prompt = ClaudeCodeTranscript.Compose("  ", [AiMessage.User("Hi")]);

        Assert.StartsWith("You are HAMMOR", prompt.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("no tools", prompt.SystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Very_long_instructions_move_off_the_command_line()
    {
        var instructions = new string('x', ClaudeCodeTranscript.MaxSystemPromptChars + 1);

        var prompt = ClaudeCodeTranscript.Compose(instructions, [AiMessage.User("Hi")]);

        Assert.True(prompt.SystemPrompt.Length < ClaudeCodeTranscript.MaxSystemPromptChars);
        Assert.StartsWith("<instructions>\n" + instructions + "\n</instructions>", prompt.Prompt, StringComparison.Ordinal);
        Assert.EndsWith("Hi", prompt.Prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_conversation_is_refused()
    {
        Assert.Throws<ArgumentException>(() => ClaudeCodeTranscript.Compose("Be HAMMOR.", []));
    }
}
