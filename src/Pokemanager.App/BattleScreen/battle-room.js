// Pokemanager battle room: feeds Pokémon Showdown's battle engine with the lines of a room battle and turns the player's
// clicks into Showdown choices ("move 1 mega", "switch 3", "team 2"). The app calls PM.init/add/request/state and gets
// {type: "ready" | "choose" | "forfeit" | "mute"} back as web messages.
'use strict';

(function () {
  // Game type order, to name the types of the player's ROM (the move menu shows the ROM's type, not Showdown's).
  const TYPES = ['Normal', 'Fighting', 'Flying', 'Poison', 'Ground', 'Rock', 'Bug', 'Ghost', 'Steel', 'Fire', 'Water', 'Grass',
    'Electric', 'Psychic', 'Ice', 'Dragon', 'Dark', 'Fairy'];

  const post = message => {
    const text = JSON.stringify(message);
    if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(text);
    else if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.webview) window.webkit.messageHandlers.webview.postMessage(text);
    else if (window.invokeCSharpAction) window.invokeCSharpAction(text);
    else console.log('[to app]', text);
  };

  const esc = text => String(text == null ? '' : text).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
  const format = (template, ...args) => String(template).replace(/\{(\d+)\}/g, (_, i) => args[+i] != null ? args[+i] : '');

  const strings = {
    whatDo: 'What will {0} do?', attack: 'Attack', switchTitle: 'Switch', mega: 'Mega Evolution', zPower: 'Z-Power',
    ultraBurst: 'Ultra Burst', waiting: 'Waiting for your opponent…', teamPreview: 'Choose the Pokémon that goes first',
    forceSwitch: 'Choose a Pokémon to send out', trapped: 'You cannot switch out.', forfeit: 'Forfeit',
    forfeitConfirm: 'Forfeit this battle?', soundOn: 'Sound: on', soundOff: 'Sound: off', loading: 'Loading the battle…',
    offline: 'The battle screen could not be loaded from play.pokemonshowdown.com. Check the internet connection.',
    struggle: 'Struggle',
  };

  let battle = null;
  let tooltips = null;
  let mySide = 'p1';
  let request = null;
  let chosen = false;
  let finished = null;
  let muted = false;
  let romMoveTypes = {};
  const controls = document.querySelector('.battle-controls');
  const forfeitButton = document.getElementById('forfeit');
  const muteButton = document.getElementById('mute');

  function loaded() {
    return typeof window.Battle === 'function' && typeof window.$ === 'function' && typeof window.Dex === 'object';
  }

  function create() {
    battle = new Battle({ id: 'pokemanager', $frame: $('.battle'), $logFrame: $('.battle-log') });
    if (mySide !== 'p1') battle.setViewpoint(mySide);
    battle.subscribe(state => {
      if (state === 'atqueueend') render();
    });
    battle.setMute(muted);
    if (typeof BattleTooltips === 'function') {
      tooltips = new BattleTooltips(battle);
      tooltips.listen($('#room'));
    }
    battle.play();
  }

  // ------------------------------------------------------------------ decisions

  function moveType(move) {
    const data = Dex.moves.get(move.id || move.move);
    const rom = data && data.num && romMoveTypes[data.num];
    if (rom != null && TYPES[rom]) return TYPES[rom];
    return data && data.exists ? data.type : '';
  }

  function hpBar(pokemon) {
    if (!pokemon.maxhp) return '';
    const ratio = pokemon.hp / pokemon.maxhp;
    const color = ratio > 0.5 ? 'hpbar' : ratio > 0.2 ? 'hpbar hpbar-yellow' : 'hpbar hpbar-red';
    return `<span class="${color}"><span style="width:${Math.round(ratio * 92) || 1}px"></span></span>`;
  }

  function serverPokemon() {
    return request.side.pokemon.map(p => {
      const mon = battle.parseDetails(p.ident.substr(4), p.ident, p.details, Object.assign({}, p));
      battle.parseHealth(p.condition, mon);
      return mon;
    });
  }

  function partyButton(mon, i, name, enabled) {
    const status = mon.status ? `<span class="status ${esc(mon.status)}"></span>` : '';
    const icon = `<span class="picon" style="${Dex.getPokemonIcon(mon)}"></span>`;
    const hp = mon.fainted ? '' : hpBar(mon) + status;
    const tooltip = `switchpokemon|${i}`;
    return enabled
      ? `<button name="${name}" value="${i}" class="has-tooltip" data-tooltip="${tooltip}">${icon}${esc(mon.name)}${hp}</button> `
      : `<button disabled class="disabled has-tooltip" data-tooltip="${tooltip}">${icon}${esc(mon.name)}${hp}</button> `;
  }

  function whatDo(title) {
    return `<div class="whatdo">${title}</div>`;
  }

  function render() {
    if (!battle) return;
    if (finished) {
      controls.innerHTML = `<div class="pm-result">${finished}</div>`;
      forfeitButton.hidden = true;
      return;
    }
    if (!request || chosen) {
      controls.innerHTML = request || battle.started ? whatDo(esc(strings.waiting)) : whatDo(esc(strings.loading));
      return;
    }
    if (!battle.atQueueEnd) return; // shown once the animations of the turn are over
    if (request.wait) {
      controls.innerHTML = whatDo(esc(strings.waiting));
      return;
    }

    const party = serverPokemon();
    battle.myPokemon = party;
    const alive = (mon, i) => !mon.fainted && !mon.active && !(mon.condition || '').endsWith(' fnt');

    if (request.teamPreview) {
      controls.innerHTML = '<div class="switchcontrols">' + whatDo(esc(strings.teamPreview)) +
        '<div class="switchmenu">' + party.map((mon, i) => partyButton(mon, i, 'team', true)).join('') + '</div></div>';
      return;
    }

    if (request.forceSwitch) {
      controls.innerHTML = '<div class="switchcontrols">' + whatDo(esc(strings.forceSwitch)) +
        '<div class="switchmenu">' + party.map((mon, i) => partyButton(mon, i, 'switch', alive(mon, i))).join('') + '</div></div>';
      return;
    }

    if (request.active) {
      const active = request.active[0];
      const current = party.find(p => p.active) || party[0];
      let moves = '';
      let zMoves = '';
      active.moves.forEach((move, i) => {
        const type = moveType(move);
        const disabled = move.disabled || (move.pp === 0 && !active.maxMoves);
        const pp = move.maxpp ? `${move.pp}/${move.maxpp}` : '&ndash;';
        const name = esc(move.move);
        const tooltip = esc(`move|${move.id}|0`);
        moves += disabled
          ? `<button disabled class="movebutton has-tooltip" data-tooltip="${tooltip}">${name}<br /><small class="type">${esc(type)}</small> <small class="pp">${pp}</small>&nbsp;</button> `
          : `<button class="movebutton type-${esc(type)} has-tooltip" name="move" value="${i + 1}" data-tooltip="${tooltip}">${name}<br /><small class="type">${esc(type)}</small> <small class="pp">${pp}</small>&nbsp;</button> `;
        const z = active.canZMove && active.canZMove[i];
        zMoves += z
          ? `<button class="movebutton type-${esc(type)} has-tooltip" name="move" value="${i + 1}" data-z="1" data-tooltip="${esc(`zmove|${move.id}|0`)}">${esc(z.move)}<br /><small class="type">${esc(type)}</small> <small class="pp">1/1</small>&nbsp;</button> `
          : '<button class="movebutton" disabled>&nbsp;</button> ';
      });
      if (!active.moves.length) {
        moves = `<button class="movebutton" name="move" value="1">${esc(strings.struggle)}<br /><small class="type">Normal</small> <small class="pp">&ndash;</small>&nbsp;</button>`;
      }
      const boxes = [];
      if (active.canMegaEvo) boxes.push(`<label class="megaevo"><input type="checkbox" name="mega" />&nbsp;${esc(strings.mega)}</label>`);
      if (active.canZMove) boxes.push(`<label class="megaevo"><input type="checkbox" name="zmove" />&nbsp;${esc(strings.zPower)}</label>`);
      if (active.canUltraBurst) boxes.push(`<label class="megaevo"><input type="checkbox" name="ultra" />&nbsp;${esc(strings.ultraBurst)}</label>`);

      const trapped = active.trapped || active.maybeTrapped;
      const switches = trapped
        ? `<em>${esc(strings.trapped)}</em>`
        : party.map((mon, i) => partyButton(mon, i, 'switch', alive(mon, i))).join('');

      controls.innerHTML =
        whatDo(esc(format(strings.whatDo, current ? current.name : ''))) +
        '<div class="movecontrols"><div class="moveselect"><button name="selectMove">' + esc(strings.attack) + '</button></div>' +
        '<div class="movemenu"><div class="pm-moves">' + moves + '</div><div class="pm-zmoves" hidden>' + zMoves + '</div>' +
        '<div style="clear:left"></div>' + (boxes.length ? '<div class="megaevo-box">' + boxes.join('') + '</div>' : '') + '</div></div>' +
        '<div class="switchcontrols"><div class="switchselect"><button name="selectSwitch">' + esc(strings.switchTitle) + '</button></div>' +
        '<div class="switchmenu">' + switches + '</div></div>';
    }
  }

  function choose(choice) {
    BattleTooltips && BattleTooltips.hideTooltip && BattleTooltips.hideTooltip();
    chosen = true;
    post({ type: 'choose', choice });
    render();
  }

  controls.addEventListener('change', e => {
    if (e.target.name !== 'zmove') return;
    const z = e.target.checked;
    controls.querySelector('.pm-moves').hidden = z;
    controls.querySelector('.pm-zmoves').hidden = !z;
  });

  controls.addEventListener('click', e => {
    const button = e.target.closest('button[name]');
    if (!button || button.disabled || !request || chosen) return;
    const value = button.value;
    const checked = name => { const box = controls.querySelector(`input[name=${name}]`); return !!(box && box.checked); };
    switch (button.name) {
      case 'move': {
        let choice = `move ${value}`;
        if (checked('mega')) choice += ' mega';
        if (button.dataset.z) choice += ' zmove';
        if (checked('ultra')) choice += ' ultra';
        choose(choice);
        break;
      }
      case 'switch':
        choose(`switch ${+value + 1}`);
        break;
      case 'team':
        choose(`team ${+value + 1}`);
        break;
    }
  });

  forfeitButton.addEventListener('click', () => {
    if (finished) return;
    if (window.confirm(strings.forfeitConfirm)) post({ type: 'forfeit' });
  });

  muteButton.addEventListener('click', () => {
    muted = !muted;
    if (battle) battle.setMute(muted);
    muteButton.textContent = muted ? strings.soundOff : strings.soundOn;
    post({ type: 'mute', muted });
  });

  // ------------------------------------------------------------------ calls from the app

  window.PM = {
    /** side: "p1"/"p2"; strings: translations; moveTypes: {move num: ROM type index} of the player's team; muted. */
    init(options) {
      Object.assign(strings, options.strings || {});
      mySide = options.side || 'p1';
      romMoveTypes = options.moveTypes || {};
      muted = !!options.muted;
      forfeitButton.textContent = strings.forfeit;
      muteButton.textContent = muted ? strings.soundOff : strings.soundOn;
      if (!loaded()) {
        const offline = document.getElementById('offline');
        offline.textContent = strings.offline;
        offline.hidden = false;
        return false;
      }
      if (!battle) create();
      render();
      return true;
    },

    /** New protocol lines, in order. */
    add(lines) {
      if (!battle) return;
      for (const line of lines) battle.add(line);
    },

    /** What the player has to decide now (Showdown's request JSON), or null. */
    request(json) {
      request = json ? JSON.parse(json) : null;
      chosen = false;
      // Showdown sends a turn's request just before the lines of that turn: wait for them, so the buttons do not flash
      // before the animations.
      setTimeout(render, 150);
    },

    /** The battle is over: shows the result and stops taking decisions. */
    finish(text) {
      finished = `<strong>${esc(text)}</strong>`;
      request = null;
      render();
    },
  };

  if (!loaded()) {
    // A script from play.pokemonshowdown.com did not load (no connection): the app still calls PM.init and gets false.
    document.getElementById('offline').textContent = strings.offline;
    document.getElementById('offline').hidden = false;
  }
  post({ type: 'ready' });
})();
