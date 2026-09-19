using Pokemanager.Model.Dump;

namespace Pokemanager.Model.Data;

/// <summary>How much of a fight a trainer is meant to be, which the game does not store anywhere.</summary>
public enum TrainerDifficulty
{
    /// <summary>The trainers of the route, the gyms and the buildings: most of the game.</summary>
    Regular,

    /// <summary>The rival, the friend who keeps challenging you, the team bosses and the story characters.</summary>
    Important,

    /// <summary>Gym leaders and kahunas, the Elite Four, the Champion and the post-game bosses.</summary>
    Boss,
}

/// <summary>
/// What each trainer is in the story — gym leader, Elite Four, rival, a character with a name — so the tab can group
/// them by how hard they are meant to be even when the ROM has randomized every trainer and class name.
/// </summary>
/// <remarks>
/// The lists are UPR ZX's own (<c>Gen6Constants.tagTrainersXY/ORAS</c>, <c>Gen7Constants.tagTrainersSM/USUM</c>), read
/// out of the bundled jar: it is the same GPL-3 project this one builds on, and the ids are the game's, not a guess.
/// Trainers with no tag are the ordinary ones. **Lillie never battles the player in Sun/Moon or Ultra Sun/Ultra Moon**,
/// so there is no entry for her anywhere.
/// </remarks>
public static class TrainerRoles
{
    /// <summary>X: 151 trainers with a part in the story.</summary>
    private const string XyTags =
        "6:GYM1-LEADER 21:GYM3-LEADER 22:GYM4-LEADER 23:GYM5-LEADER 24:GYM6-LEADER 25:GYM7-LEADER 26:GYM8-LEADER 28:GYM5 " +
        "29:GYM5 30:GYM5 31:GYM8 32:GYM8 39:GYM1 40:GYM1 48:GYM1 63:GYM2 64:GYM2 76:GYM2-LEADER 83:GYM3 84:GYM3 105:GYM2 " +
        "106:GYM2 121:GYM4 122:GYM4 123:GYM4 124:GYM4 130:RIVAL2-0 131:RIVAL2-1 132:RIVAL2-2 137:FRIEND1-0 138:FRIEND1-1 " +
        "139:FRIEND1-2 146:GYM3 147:GYM3 168:GYM8 169:GYM8 170:GYM7 171:GYM7 172:GYM7 174:STRONG 175:STRONG 184:RIVAL4-0 " +
        "185:RIVAL4-1 186:RIVAL4-2 187:ELITE3 188:NOTSTRONG 243:GYM6 245:GYM6 248:GYM6 250:GYM6 269:ELITE1 270:ELITE4 " +
        "271:ELITE2 276:CHAMPION 303:THEMED:LYSANDRE-LEADER 304:STRONG 321:FRIEND2-0 322:FRIEND2-1 323:FRIEND2-2 324:STRONG " +
        "325:STRONG 327:STRONG 328:STRONG 329:RIVAL3-0 330:RIVAL3-1 331:RIVAL3-2 332:RIVAL5-0 333:RIVAL5-1 334:RIVAL5-2 " +
        "335:RIVAL7-0 336:RIVAL7-1 337:RIVAL7-2 338:RIVAL8-0 339:RIVAL8-1 340:RIVAL8-2 341:RIVAL9-0 342:RIVAL9-1 343:RIVAL9-2 " +
        "344:STRONG 345:STRONG 346:STRONG 347:STRONG 348:STRONG 349:STRONG 350:STRONG 351:STRONG 365:GYM7 366:GYM7 " +
        "435:RIVAL1-0 436:RIVAL1-1 437:RIVAL1-2 438:STRONG 439:STRONG 461:GYM5 462:GYM5 463:GYM5 464:GYM5 465:GYM5 466:GYM5 " +
        "467:GYM5 468:GYM5 469:GYM5 470:STRONG 471:STRONG 472:STRONG 473:STRONG 474:STRONG 475:STRONG 476:STRONG 477:STRONG " +
        "478:STRONG 479:STRONG 519:RIVAL10-0 520:RIVAL10-1 521:RIVAL10-2 525:THEMED:LYSANDRE-LEADER " +
        "526:THEMED:LYSANDRE-LEADER 573:STRONG 575:RIVAL2-0 576:RIVAL2-1 577:RIVAL2-2 578:RIVAL4-0 579:RIVAL4-1 580:RIVAL4-2 " +
        "581:RIVAL3-0 582:RIVAL3-1 583:RIVAL3-2 584:RIVAL5-0 585:RIVAL5-1 586:RIVAL5-2 587:RIVAL7-0 588:RIVAL7-1 589:RIVAL7-2 " +
        "590:RIVAL8-0 591:RIVAL8-1 592:RIVAL8-2 593:RIVAL9-0 594:RIVAL9-1 595:RIVAL9-2 596:RIVAL1-0 597:RIVAL1-1 598:RIVAL1-2 " +
        "599:RIVAL10-0 600:RIVAL10-1 601:RIVAL10-2 604:RIVAL6-0 605:RIVAL6-1 606:RIVAL6-2 607:RIVAL6-0 608:RIVAL6-1 " +
        "609:RIVAL6-2";

    /// <summary>ORAS: 126 trainers with a part in the story.</summary>
    private const string OrasTags =
        "1:RIVAL1-0 2:RIVAL1-1 3:RIVAL1-2 4:RIVAL1-0 5:RIVAL1-1 6:RIVAL1-2 22:GYM1 34:GYM3 35:GYM3 56:GYM2 59:GYM2 60:GYM2 " +
        "63:GYM5 64:GYM5 65:GYM5 66:GYM5 67:GYM5 68:GYM5 69:GYM5 81:GYM4 83:GYM4 85:GYM4 115:GYM6 118:GYM6 157:GYM7 158:GYM7 " +
        "159:GYM7 178:THEMED:ARCHIE-LEADER 225:GYM7 226:GYM7 231:THEMED:ARCHIE-LEADER 235:THEMED:MAXIE-LEADER " +
        "236:THEMED:MAXIE-LEADER 266:THEMED:ARCHIE-LEADER 271:THEMED:MAXIE-LEADER 289:RIVAL2-0 290:RIVAL2-1 291:RIVAL2-2 " +
        "292:RIVAL4-0 293:RIVAL4-1 294:RIVAL4-2 295:RIVAL2-0 296:RIVAL2-1 297:RIVAL2-2 298:RIVAL4-0 299:RIVAL4-1 300:RIVAL4-2 " +
        "320:GYM7 338:GYM8 339:GYM8 340:GYM8 341:GYM8 342:GYM8 516:GYM6 517:GYM6 518:THEMED:WALLY-STRONG 527:RIVAL5-0 " +
        "528:RIVAL5-1 529:RIVAL5-2 530:RIVAL5-0 531:RIVAL5-1 532:RIVAL5-2 552:GYM7-LEADER 553:ELITE1 554:ELITE2 555:ELITE3 " +
        "556:ELITE4 557:CHAMPION 561:GYM1-LEADER 562:GYM1 563:GYM2-LEADER 567:GYM3-LEADER 568:GYM3 569:GYM4-LEADER " +
        "570:GYM5-LEADER 571:GYM6-LEADER 572:GYM8-LEADER 583:THEMED:WALLY-STRONG 594:GYM8 613:GYM4 614:GYM3 615:GYM4 646:GYM8 " +
        "647:GYM8 667:GYM1 674:RIVAL3-0 675:RIVAL3-1 676:RIVAL3-2 677:RIVAL3-0 678:RIVAL3-1 679:RIVAL3-2 680:CHAMPION " +
        "683:THEMED:MATT-STRONG 684:THEMED:MATT-STRONG 685:THEMED:MATT-STRONG 686:THEMED:MATT-STRONG 687:THEMED:MATT-STRONG " +
        "688:THEMED:SHELLY-STRONG 689:THEMED:SHELLY-STRONG 690:THEMED:SHELLY-STRONG 691:THEMED:TABITHA-STRONG " +
        "692:THEMED:TABITHA-STRONG 693:THEMED:TABITHA-STRONG 694:THEMED:COURTNEY-STRONG 695:THEMED:COURTNEY-STRONG " +
        "696:THEMED:COURTNEY-STRONG 697:THEMED:COURTNEY-STRONG 698:THEMED:COURTNEY-STRONG 699:RIVAL6-0 700:RIVAL6-1 " +
        "701:RIVAL6-2 730:GYM6 823:GYM4 824:GYM4 906:RIVAL6-0 907:RIVAL6-1 908:RIVAL6-2 909:ELITE1 910:ELITE2 911:ELITE3 " +
        "912:ELITE4 913:CHAMPION 942:CHAMPION 943:GYM8-LEADER 944:THEMED:WALLY-STRONG 946:THEMED:WALLY-STRONG";

    /// <summary>SM: 94 trainers with a part in the story.</summary>
    private const string SmTags =
        "6:FRIEND1-0 7:FRIEND1-1 8:FRIEND1-2 9:FRIEND2-0 10:FRIEND2-1 11:FRIEND2-2 12:FRIEND3-0 13:FRIEND3-1 14:FRIEND3-2 " +
        "23:ELITE1 52:THEMED:ILIMA-STRONG 74:THEMED:DEXIO-STRONG 75:THEMED:SINA-STRONG 76:FRIEND4-0 77:FRIEND4-1 78:FRIEND4-2 " +
        "79:THEMED:GLADION-STRONG 82:FRIEND5-0 83:FRIEND5-1 84:FRIEND5-2 89:THEMED:PLUMERIA-STRONG 90:ELITE2 129:RIVAL2-0 " +
        "131:THEMED:LUSAMINE-LEADER 132:THEMED:FABA-STRONG 138:THEMED:GUZMA-LEADER 144:THEMED:LANA-STRONG " +
        "146:THEMED:MALLOW-STRONG 149:ELITE5 152:ELITE1 153:ELITE2 154:ELITE3 155:ELITE4 156:ELITE6 " +
        "158:THEMED:LUSAMINE-LEADER 167:THEMED:MOLAYNE-STRONG 185:THEMED:GLADION-STRONG 215:THEMED:ILIMA-STRONG " +
        "216:THEMED:ILIMA-STRONG 217:FRIEND7-0 218:FRIEND7-1 219:FRIEND7-2 220:FRIEND8-0 221:FRIEND8-1 222:FRIEND8-2 " +
        "235:THEMED:GUZMA-LEADER 236:THEMED:GUZMA-LEADER 238:THEMED:PLUMERIA-STRONG 239:THEMED:GLADION-STRONG " +
        "240:THEMED:GLADION-STRONG 241:THEMED:FABA-STRONG 349:ELITE1 350:ELITE5 351:ELITE2 352:ELITE6 356:FRIEND12-0 " +
        "357:FRIEND12-1 358:FRIEND12-2 359:ELITE4 360:THEMED:FABA-STRONG 396:THEMED:ILIMA-STRONG 398:THEMED:KIAWE-STRONG " +
        "400:THEMED:GUZMA-LEADER 401:THEMED:PLUMERIA-STRONG 403:ELITE3 405:THEMED:SOPHOCLES-STRONG 410:THEMED:FABA-STRONG " +
        "412:THEMED:DEXIO-STRONG 413:RIVAL2-1 414:RIVAL2-2 415:THEMED:GLADION-STRONG 416:THEMED:GLADION-STRONG " +
        "417:THEMED:GLADION-STRONG 418:THEMED:GLADION-STRONG 419:THEMED:GLADION-STRONG 435:THEMED:MINA-STRONG 438:FRIEND6-0 " +
        "439:FRIEND6-1 440:FRIEND6-2 441:THEMED:GLADION-STRONG 447:FRIEND9-0 448:FRIEND9-1 449:FRIEND9-2 450:FRIEND10-0 " +
        "451:FRIEND10-1 452:FRIEND10-2 467:THEMED:MINA-STRONG 477:RIVAL3-0 478:RIVAL3-1 479:RIVAL3-2 " +
        "481:THEMED:MOLAYNE-STRONG 482:FRIEND11-0 483:FRIEND11-1 484:FRIEND11-2";

    /// <summary>USUM: 114 trainers with a part in the story.</summary>
    private const string UsumTags =
        "9:FRIEND2-0 10:FRIEND2-1 11:FRIEND2-2 12:FRIEND3-0 13:FRIEND3-1 14:FRIEND3-2 23:ELITE1 52:THEMED:ILIMA-STRONG " +
        "74:THEMED:DEXIO-STRONG 75:THEMED:SINA-STRONG 76:FRIEND4-0 77:FRIEND4-1 78:FRIEND4-2 79:THEMED:GLADION-STRONG " +
        "82:FRIEND5-0 83:FRIEND5-1 84:FRIEND5-2 89:THEMED:PLUMERIA-STRONG 90:ELITE2 131:THEMED:LUSAMINE-LEADER " +
        "132:THEMED:FABA-STRONG 138:THEMED:GUZMA-LEADER 144:THEMED:LANA-STRONG 146:THEMED:MALLOW-STRONG 149:ELITE6 153:ELITE2 " +
        "154:ELITE3 156:ELITE7 185:THEMED:GLADION-STRONG 215:THEMED:ILIMA-STRONG 216:THEMED:ILIMA-STRONG 217:FRIEND7-0 " +
        "218:FRIEND7-1 219:FRIEND7-2 220:FRIEND8-0 221:FRIEND8-1 222:FRIEND8-2 235:THEMED:GUZMA-LEADER " +
        "236:THEMED:GUZMA-LEADER 238:THEMED:PLUMERIA-STRONG 239:THEMED:GLADION-STRONG 240:THEMED:GLADION-STRONG " +
        "241:THEMED:FABA-STRONG 350:ELITE6 351:ELITE2 352:ELITE7 356:FRIEND12-0 357:FRIEND12-1 358:FRIEND12-2 359:ELITE4 " +
        "396:THEMED:ILIMA-STRONG 398:THEMED:KIAWE-STRONG 401:THEMED:PLUMERIA-STRONG 405:THEMED:SOPHOCLES-STRONG " +
        "410:THEMED:FABA-STRONG 412:THEMED:DEXIO-STRONG 415:THEMED:GLADION-STRONG 416:THEMED:GLADION-STRONG " +
        "417:THEMED:GLADION-STRONG 418:THEMED:GLADION-STRONG 419:THEMED:GLADION-STRONG 438:FRIEND6-0 439:FRIEND6-1 " +
        "440:FRIEND6-2 441:THEMED:GLADION-STRONG 447:FRIEND9-0 448:FRIEND9-1 449:FRIEND9-2 450:FRIEND10-0 451:FRIEND10-1 " +
        "452:FRIEND10-2 477:RIVAL2-0 478:RIVAL2-1 479:RIVAL2-2 489:ELITE5 490:ELITE5 491:FRIEND1-0 492:FRIEND1-1 " +
        "493:FRIEND1-2 494:FRIEND11-0 495:FRIEND11-1 496:FRIEND11-2 497:ELITE4 498:THEMED:SOLIERA-STRONG " +
        "499:THEMED:SOLIERA-STRONG 500:THEMED:DULSE-STRONG 501:THEMED:DULSE-STRONG 502:THEMED:ILIMA-STRONG " +
        "503:THEMED:LANA-STRONG 504:THEMED:KIAWE-STRONG 505:THEMED:MALLOW-STRONG 506:THEMED:SOPHOCLES-STRONG " +
        "507:THEMED:MINA-STRONG 508:ELITE3 541:UBER 542:UBER 543:UBER 558:THEMED:GUZMA-LEADER 559:UBER 560:UBER " +
        "561:THEMED:FABA-STRONG 562:UBER 572:UBER 573:UBER 580:UBER 623:THEMED:DEXIO-STRONG 644:THEMED:LUSAMINE-LEADER " +
        "645:UBER 647:THEMED:GUZMA-LEADER 648:THEMED:SOLIERA-STRONG 649:THEMED:DULSE-STRONG 650:ELITE1 " +
        "651:THEMED:SOLIERA-STRONG 652:THEMED:DULSE-STRONG";
    private static readonly Dictionary<GameFamily, Dictionary<int, string>> ByFamily = new()
    {
        [GameFamily.XY] = Parse(XyTags),
        [GameFamily.ORAS] = Parse(OrasTags),
        [GameFamily.SM] = Parse(SmTags),
        [GameFamily.USUM] = Parse(UsumTags),
    };

    /// <summary>Trainer id → its part in the story ("GYM3-LEADER", "ELITE1", "RIVAL5-2", "THEMED:GUZMA").</summary>
    public static IReadOnlyDictionary<int, string> Of(GameTitle title) =>
        ByFamily.TryGetValue(title.Family(), out var tags) ? tags : new Dictionary<int, string>();

    /// <summary>The part a trainer plays, or null when it is one of the ordinary ones.</summary>
    public static string? TagOf(GameTitle title, int trainer) => Of(title).GetValueOrDefault(trainer);

    /// <summary>
    /// How hard the trainer is meant to be. The rule is UPR ZX's own for bosses (a leader, the Elite Four, the Champion
    /// or a post-game boss) and for the ones it calls important (rival, friend, strong), plus the named characters
    /// (<c>THEMED:</c>), which are the team bosses and the trial captains.
    /// </summary>
    public static TrainerDifficulty DifficultyOf(string? tag) => tag switch
    {
        null => TrainerDifficulty.Regular,
        _ when tag.EndsWith("-LEADER", StringComparison.Ordinal) => TrainerDifficulty.Boss,
        _ when tag.StartsWith("ELITE", StringComparison.Ordinal) => TrainerDifficulty.Boss,
        _ when tag.StartsWith("CHAMPION", StringComparison.Ordinal) => TrainerDifficulty.Boss,
        _ when tag.StartsWith("UBER", StringComparison.Ordinal) => TrainerDifficulty.Boss,
        _ when tag.StartsWith("RIVAL", StringComparison.Ordinal) => TrainerDifficulty.Important,
        _ when tag.StartsWith("FRIEND", StringComparison.Ordinal) => TrainerDifficulty.Important,
        _ when tag.StartsWith("THEMED:", StringComparison.Ordinal) => TrainerDifficulty.Important,
        "STRONG" => TrainerDifficulty.Important,
        _ => TrainerDifficulty.Regular,
    };

    /// <summary>The difficulty of a trainer of a game.</summary>
    public static TrainerDifficulty DifficultyOf(GameTitle title, int trainer) => DifficultyOf(TagOf(title, trainer));

    private static Dictionary<int, string> Parse(string packed)
    {
        var tags = new Dictionary<int, string>();
        foreach (string pair in packed.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = pair.IndexOf(':', StringComparison.Ordinal);
            // "THEMED:GUZMA" has a colon of its own: the id ends at the first one.
            if (colon > 0 && int.TryParse(pair[..colon], out int id))
                tags[id] = pair[(colon + 1)..];
        }
        return tags;
    }
}
