using System.Collections.Generic;

namespace HitEducation.App;

public sealed class RandomPickerState
{
	public string ActiveRosterId { get; set; } = string.Empty;

	public List<RandomPickerRoster> Rosters { get; set; } = [];

	public List<RandomPickerMember> Members { get; set; } = [];

	public List<RandomPickerMember> ActiveMembers => Rosters.Find(x => x.Id == ActiveRosterId)?.Members ?? Members;
}
