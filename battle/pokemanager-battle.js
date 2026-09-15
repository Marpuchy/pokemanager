// Pokemanager battle host: runs Pokémon Showdown's simulator (MIT) with the data of each player's ROM.
//
// Protocol: one JSON object per line.
//   in  {"type":"start","rules":{...},"players":[{team}, {team}],"avatars":["red","blue"]}   once, first
//   in  {"type":"choose","side":"p1","choice":"move 1"}             a player's decision
//   out {"type":"update","lines":[...]}                             what everyone sees
//   out {"type":"side","side":"p1","lines":[...]}                   a player's private request/errors
//   out {"type":"end","winner":"Ash"} / {"type":"error","message":...}
//
// Each team carries its own ROM data: base stats and types of its species and forms, and the data of its moves.
// A rule of our own applies them per side (ModifySpecies, ModifyType, ModifyMove, ModifyPriority), so two players with
// different randomized ROMs battle with their own numbers and megas/formes keep working.
'use strict';

const path = require('path');
const readline = require('readline');
const { Dex, BattleStream, Teams, TeamValidator } = require(path.join(__dirname, 'node_modules', 'pokemon-showdown'));

// Game order of types and natures (Showdown stores them by name).
const TYPES = ['Normal', 'Fighting', 'Flying', 'Poison', 'Ground', 'Rock', 'Bug', 'Ghost', 'Steel', 'Fire', 'Water', 'Grass',
  'Electric', 'Psychic', 'Ice', 'Dragon', 'Dark', 'Fairy'];
const NATURES = ['Hardy', 'Lonely', 'Brave', 'Adamant', 'Naughty', 'Bold', 'Docile', 'Relaxed', 'Impish', 'Lax', 'Timid', 'Hasty',
  'Serious', 'Jolly', 'Naive', 'Modest', 'Mild', 'Quiet', 'Bashful', 'Rash', 'Calm', 'Gentle', 'Sassy', 'Careful', 'Quirky'];
const CATEGORIES = ['Status', 'Physical', 'Special'];

const send = obj => process.stdout.write(JSON.stringify(obj) + '\n');

function fail(message) {
  send({ type: 'error', message });
  process.exit(1);
}

/** Per side: "num-form" → {stats, types} and move num → ROM move data. */
const rom = {};
let rules = {};

function speciesKey(dex, species) {
  const base = dex.species.get(species.baseSpecies);
  const form = Math.max(0, (base.formeOrder || [base.name]).indexOf(species.name));
  return `${species.num}-${form}`;
}

Dex.data.Rulesets.pokemanagerromdata = {
  effectType: 'Rule',
  name: 'Pokemanager ROM Data',
  desc: "Each player's Pokémon and moves use the data of that player's ROM.",
  onModifySpecies(species, target) {
    const data = target && rom[target.side.id] && rom[target.side.id].species[speciesKey(this.dex, species)];
    if (!data) return;
    const clone = this.dex.deepClone(species);
    clone.baseStats = { ...data.stats };
    clone.types = [...data.types];
    return clone;
  },
  onBegin() {
    // Custom Game is a debug format and reports everyone's exact HP: back to what a player sees in the game.
    this.reportExactHP = false;
    this.reportPercentages = true;
    for (const side of this.sides) {
      const team = rom[side.id];
      side.pokemon.forEach((pokemon, i) => {
        const mon = team.mons[i];
        if (!mon) return;
        pokemon.moveSlots.forEach((slot, m) => {
          const pp = mon.pp && mon.pp[m];
          if (!pp) return;
          slot.maxpp = pp[1];
          slot.pp = rules.healBefore ? pp[1] : Math.min(pp[0], pp[1]);
        });
        pokemon.baseMoveSlots = pokemon.moveSlots.map(s => ({ ...s }));
        if (!rules.healBefore && mon.hp >= 0) pokemon.sethp(Math.max(1, Math.min(mon.hp, pokemon.maxhp)));
      });
    }
  },
  onModifyTypePriority: -100,
  onModifyType(move, pokemon) {
    const data = pokemon && rom[pokemon.side.id] && rom[pokemon.side.id].moves[move.num];
    const vanilla = this.dex.moves.get(move.id);
    // Only randomized types, and not when the move changed its own type this turn (Hidden Power, Weather Ball…).
    if (data && data.type !== vanilla.type && move.type === vanilla.type) move.type = data.type;
  },
  onModifyMove(move, pokemon) {
    const data = pokemon && rom[pokemon.side.id] && rom[pokemon.side.id].moves[move.num];
    if (!data) return;
    const vanilla = this.dex.moves.get(move.id);
    if (vanilla.basePower > 0 && data.power > 0 && move.basePower === vanilla.basePower) move.basePower = data.power;
    if (data.accuracy !== vanilla.accuracy && move.accuracy === vanilla.accuracy) move.accuracy = data.accuracy;
    if (data.category !== vanilla.category && vanilla.category !== 'Status' && data.category !== 'Status') move.category = data.category;
  },
  onModifyPriority(priority, pokemon, target, move) {
    const data = pokemon && move && rom[pokemon.side.id] && rom[pokemon.side.id].moves[move.num];
    if (!data) return;
    const vanilla = this.dex.moves.get(move.id);
    if (data.priority !== vanilla.priority) return priority + data.priority - vanilla.priority;
  },
};

/** Game ids of a team → a Showdown team, and its ROM data indexed for the rule. */
function prepareTeam(dex, side, team) {
  const species = {};
  for (const s of team.species) {
    // Game stat order: HP, Atk, Def, Spe, SpA, SpD.
    const [hp, atk, def, spe, spa, spd] = s.baseStats;
    species[`${s.species}-${s.form}`] = { stats: { hp, atk, def, spa, spd, spe }, types: [...new Set(s.types.map(t => TYPES[t] || 'Normal'))] };
  }
  const moves = {};
  for (const m of team.moves) {
    moves[m.id] = {
      type: TYPES[m.type] || 'Normal',
      category: CATEGORIES[m.category] || 'Physical',
      power: m.power,
      accuracy: m.accuracy >= 101 || m.accuracy === 0 ? true : m.accuracy,
      priority: m.priority,
    };
  }
  rom[side] = { species, moves, mons: team.mons };

  const byNum = (list, num) => list.find(x => x.num === num && !x.isNonstandard) || list.find(x => x.num === num);
  const allSpecies = dex.species.all(), allMoves = dex.moves.all(), allAbilities = dex.abilities.all(), allItems = dex.items.all();
  const sets = team.mons.map(mon => {
    const base = allSpecies.find(s => s.num === mon.species && s.name === s.baseSpecies);
    if (!base) fail(`Species #${mon.species} is not known to the simulator.`);
    const name = (base.formeOrder && base.formeOrder[mon.form]) || base.name;
    const [hp, atk, def, spe, spa, spd] = mon.ivs;
    const [ehp, eatk, edef, espe, espa, espd] = mon.evs;
    return {
      name: mon.nickname || name,
      species: name,
      item: (mon.item && byNum(allItems, mon.item) || {}).name || '',
      ability: (byNum(allAbilities, mon.ability) || {}).name || 'No Ability',
      moves: mon.moves.filter(m => m > 0).map(m => (byNum(allMoves, m) || {}).name).filter(Boolean),
      nature: NATURES[mon.nature] || 'Serious',
      gender: mon.gender === 0 ? 'M' : mon.gender === 1 ? 'F' : 'N',
      shiny: !!mon.shiny,
      // Showdown's Adjust Level rule only acts in its team validator, which is not used here: set it directly.
      level: rules.level > 0 ? rules.level : mon.level,
      happiness: mon.friendship ?? 255,
      evs: { hp: ehp, atk: eatk, def: edef, spa: espa, spd: espd, spe: espe },
      ivs: { hp, atk, def, spa, spd, spe },
    };
  });
  return Teams.pack(sets);
}

/** The options chosen before the battle → a Showdown format with rules. */
function formatOf(r) {
  // HP Percentage Mod: the opponent's HP is shown as a percentage (as the bar in the game); each player gets exact numbers
  // for their own side through Showdown's split lines.
  const list = ['Pokemanager ROM Data', 'Endless Battle Clause', 'HP Percentage Mod'];
  if (r.sleepClause) list.push('Sleep Clause Mod');
  if (r.speciesClause) list.push('Species Clause');
  if (r.itemClause) list.push('Item Clause = 1');
  if (r.ohkoClause) list.push('OHKO Clause');
  if (r.evasionClause) list.push('Evasion Moves Clause');
  if (r.batonPassClause) list.push('Baton Pass Clause');
  if (r.noZMoves) list.push('Z-Move Clause');
  if (r.noMegas) list.push('-Mega');
  if (!r.teamPreview) list.push('!Team Preview');
  return `gen${r.generation === 6 ? 6 : 7}customgame@@@${list.join(',')}`;
}

let stream;
const input = readline.createInterface({ input: process.stdin });
input.on('line', line => {
  if (!line.trim()) return;
  let msg;
  try {
    msg = JSON.parse(line);
  } catch {
    return send({ type: 'error', message: 'Not JSON: ' + line.slice(0, 80) });
  }
  if (msg.type === 'start') {
    if (stream) return send({ type: 'error', message: 'The battle already started.' });
    rules = msg.rules || {};
    const format = formatOf(rules);
    let dex;
    try {
      dex = Dex.forFormat(Dex.formats.validate(format));
    } catch (e) {
      fail('Rules not valid: ' + e.message);
    }
    const packed = msg.players.map((team, i) => prepareTeam(dex, `p${i + 1}`, team));
    // Clauses and bans (species, items, OHKO, evasion, Baton Pass, megas, Z-moves) are checked here, as Showdown does
    // before a battle: a team that breaks them does not start. (Legality is not: the format has no such rules.)
    const validator = TeamValidator.get(format);
    const invalid = packed.map((team, i) => ({ side: `p${i + 1}`, problems: validator.validateTeam(Teams.unpack(team)) || [] }))
      .filter(v => v.problems.length > 0);
    if (invalid.length > 0) {
      send({ type: 'invalid', teams: invalid });
      process.exit(0);
    }
    stream = new BattleStream();
    (async () => {
      for await (const chunk of stream) {
        const [kind, ...rest] = chunk.split('\n');
        if (kind === 'update') {
          const lines = rest;
          send({ type: 'update', lines });
          const win = lines.find(l => l.startsWith('|win|') || l === '|tie');
          if (win) send({ type: 'end', winner: win.startsWith('|win|') ? win.slice(5) : null });
        } else if (kind === 'sideupdate') {
          send({ type: 'side', side: rest[0], lines: rest.slice(1) });
        } else if (kind === 'end') {
          process.exit(0);
        }
      }
    })();
    stream.write(`>start ${JSON.stringify({ formatid: format, seed: msg.seed })}`);
    const avatars = msg.avatars || [];
    msg.players.forEach((team, i) => stream.write(`>player p${i + 1} ${JSON.stringify({ name: team.player, avatar: avatars[i] || undefined, team: packed[i] })}`));
  } else if (msg.type === 'choose' && stream) {
    if (/^p[12]$/.test(msg.side) && typeof msg.choice === 'string' && !msg.choice.includes('\n'))
      stream.write(`>${msg.side} ${msg.choice}`);
  } else if (msg.type === 'forfeit' && stream) {
    if (/^p[12]$/.test(msg.side)) stream.write(`>forcelose ${msg.side}`);
  }
});
input.on('close', () => process.exit(0));
