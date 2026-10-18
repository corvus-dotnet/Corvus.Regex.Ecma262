// <copyright file="MatchEvaluator.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Corvus.Ecma262.RegularExpressions;

/// <summary>Gives the replacement for one match.</summary>
/// <param name="match">The match.</param>
/// <returns>The replacement, which is used as it is.</returns>
public delegate string MatchEvaluator(Match match);
