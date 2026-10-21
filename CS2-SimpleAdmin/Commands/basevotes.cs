using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CS2_SimpleAdmin.Menus;
using PanoramaUiKit.Shared.Api;
using PanoramaUiKit.Shared.Votes;

namespace CS2_SimpleAdmin;

public partial class CS2_SimpleAdmin
{
    private const float VoteDurationSeconds = 30f;

    /// <summary>
    /// Handles the vote command: shows the vote screen to every player, collects one answer each
    /// and prints the results when the countdown ends.
    /// </summary>
    /// <param name="caller">The player/admin who initiated the vote, or null for console.</param>
    /// <param name="command">Command object containing question and options.</param>
    [RequiresPermissions("@css/generic")]
    [CommandHelper(minArgs: 2, usage: "<question> [... options ...]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnVoteCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (command.ArgCount < 2 || _localizer == null)
        {
            return;
        }

        Helper.LogCommand(caller, command);

        if (caller != null && SilentPlayers.Contains(caller.Slot))
        {
            return;
        }

        IPanoramaUiKit? kit = UiKit.Instance;
        if (kit == null)
        {
            return;
        }

        string question = command.GetArg(1);
        string callerName = Helper.GetCallerName(caller);
        List<string> answers = [];
        for (int i = 2; i < command.ArgCount; i++)
        {
            answers.Add(command.GetArg(i));
        }

        int[] counts = new int[answers.Count];
        Dictionary<ulong, int> ballots = [];

        VoteInProgress = true;

        foreach (CCSPlayerController player in Helper.GetValidPlayers())
        {
            kit.Votes.Show(player, BuildVote(player, question, answers, counts, null, OnVote));
            player.SendLocalizedMessage(_localizer, "sa_admin_vote_message", callerName, question);
        }

        Helper.PrintToCenterAll(_localizer["sa_admin_vote_message", callerName, question]);

        AddTimer(VoteDurationSeconds, () =>
        {
            UiKit.Instance?.Votes.HideAll(UiKit.VoteLayout);
            VoteInProgress = false;

            if (_localizer == null)
            {
                return;
            }

            foreach (CCSPlayerController player in Helper.GetValidPlayers())
            {
                player.SendLocalizedMessage(_localizer, "sa_admin_vote_message_results", question);

                for (int i = 0; i < answers.Count; i++)
                {
                    player.SendLocalizedMessage(_localizer, "sa_admin_vote_message_results_answer", answers[i], counts[i]);
                }
            }
        }, TimerFlags.STOP_ON_MAPCHANGE);

        return;

        void OnVote(VoteEvent vote)
        {
            if (!VoteInProgress || ballots.ContainsKey(vote.Player.SteamID))
            {
                return;
            }

            ballots[vote.Player.SteamID] = vote.Index;
            counts[vote.Index]++;

            IPanoramaUiKit? currentKit = UiKit.Instance;
            if (currentKit == null)
            {
                return;
            }

            foreach (CCSPlayerController player in Helper.GetValidPlayers())
            {
                int? selected = null;
                if (ballots.TryGetValue(player.SteamID, out int index))
                {
                    selected = index;
                }

                currentKit.Votes.Update(player, BuildVote(player, question, answers, counts, selected, OnVote));
            }
        }
    }

    /// <summary>
    /// The vote screen for one player, locked once that player has voted.
    /// </summary>
    private static VoteOptions BuildVote(CCSPlayerController player, string question, List<string> answers, int[] counts,
        int? selected, Action<VoteEvent> onVote)
    {
        string title;
        using (new WithTemporaryCulture(player.GetLanguage()))
        {
            title = _localizer?["sa_admin_vote_menu_title", question] ?? question;
        }

        List<VoteChoice> choices = [];
        for (int i = 0; i < answers.Count; i++)
        {
            choices.Add(new VoteChoice(answers[i], counts[i]));
        }

        return new VoteOptions
        {
            Layout = UiKit.VoteLayout,
            Title = title,
            Choices = choices,
            Selected = selected,
            Locked = selected.HasValue,
            Seconds = VoteDurationSeconds,
            OnVote = onVote
        };
    }
}
