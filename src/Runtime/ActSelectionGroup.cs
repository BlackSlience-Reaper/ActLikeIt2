using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;

namespace ActLikeIt2.Runtime;

internal sealed class ActSelectionGroup
{
	private readonly List<ActModel> _acts = new();
	private readonly List<LocString> _descriptions = new();

	public string Id { get; }

	public LocString Title { get; }

	public LocString Description { get; }

	public IReadOnlyList<ActModel> Acts => _acts;

	public ActSelectionGroup(
		string id,
		LocString title,
		LocString description,
		ActModel firstAct,
		LocString firstActDescription)
	{
		Id = id;
		Title = title;
		Description = description;
		_acts.Add(firstAct);
		_descriptions.Add(firstActDescription);
	}

	public void Add(ActModel act, LocString description)
	{
		_acts.Add(act);
		_descriptions.Add(description);
	}

	public ActSelectionGroup PreRoll(Rng rng)
	{
		if (_acts.Count == 1)
		{
			return this;
		}

		int selectedIndex = rng.NextInt(_acts.Count);
		ActModel selected = _acts[selectedIndex];
		ActLikeIt2Mod.Log.Info(
			$"[Fork] Pre-rolled group '{Id}' to '{selected.Id.Entry}' "
			+ $"from [{string.Join(",", _acts.Select(static act => act.Id.Entry))}].");
		return new ActSelectionGroup(
			Id,
			selected.Title,
			_descriptions[selectedIndex],
			selected,
			_descriptions[selectedIndex]);
	}

	public static ActSelectionGroup ForSingleAct(
		ActModel act,
		LocString description) =>
		new(act.Id.Entry, act.Title, description, act, description);
}
