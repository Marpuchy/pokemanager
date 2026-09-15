// Checks the pruned simulator copy (argv[2] = battle folder): validator, rulesets and a random battle to the end.
const path = require('path');
const ps = path.join(process.argv[2], 'node_modules', 'pokemon-showdown');
const { BattleStream, getPlayerStreams, Teams, TeamValidator, Dex } = require(ps);
const { RandomPlayerAI } = require(path.join(ps, 'dist', 'sim', 'tools', 'random-player-ai'));
const team1 = Teams.generate('gen7randombattle');
const team2 = Teams.generate('gen7randombattle');
const v = TeamValidator.get('gen7customgame@@@Species Clause,Sleep Clause Mod');
console.log('validator problems', (v.validateTeam(team1) || []).length, 'rulesets', Object.keys(Dex.data.Rulesets).length);
const stream = new BattleStream();
const s = getPlayerStreams(stream);
void new RandomPlayerAI(s.p1).start();
void new RandomPlayerAI(s.p2).start();
(async () => {
  for await (const chunk of s.omniscient) {
    const win = chunk.split('\n').find(l => l.startsWith('|win|'));
    if (win) { console.log('winner', win); process.exit(0); }
  }
})();
void s.omniscient.write(`>start {"formatid":"gen7customgame"}
>player p1 {"name":"A","team":${JSON.stringify(Teams.pack(team1))}}
>player p2 {"name":"B","team":${JSON.stringify(Teams.pack(team2))}}`);
setTimeout(() => { console.log('timeout'); process.exit(1); }, 60000);
