using System.Collections.Generic;
using System.Linq;

namespace HitEducation.App;

public sealed class RandomPickerState
{
	public string ActiveRosterId { get; set; } = string.Empty;

	public List<RandomPickerRoster> Rosters { get; set; } = [];

	public List<RandomPickerMember> Members { get; set; } = [];

	public RandomPickerRoster? ActiveRoster => Rosters.Find(x => x.Id == ActiveRosterId) ?? Rosters.FirstOrDefault();

	public List<RandomPickerMember> ActiveMembers => ActiveRoster?.Members ?? Members;
}
