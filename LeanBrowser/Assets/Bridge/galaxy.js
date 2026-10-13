(() => {
  "use strict";

  const canvas = document.getElementById("game");
  const context = canvas.getContext("2d", { alpha: false });
  if (!context) {
    document.getElementById("overlay-copy").textContent = "Não foi possível iniciar o campo de jogo neste navegador.";
    document.getElementById("start-button").disabled = true;
    return;
  }

  const WIDTH = 480;
  const HEIGHT = 640;
  const BEST_KEY = "cotton.galaxy.best.v1";
  const MUTE_KEY = "cotton.galaxy.muted.v1";
  const ui = {
    score: document.getElementById("score"),
    wave: document.getElementById("wave"),
    lives: document.getElementById("lives"),
    best: document.getElementById("best-score"),
    recordNote: document.getElementById("record-note"),
    overlay: document.getElementById("overlay"),
    eyebrow: document.getElementById("overlay-eyebrow"),
    title: document.getElementById("overlay-title"),
    copy: document.getElementById("overlay-copy"),
    hint: document.getElementById("overlay-hint"),
    start: document.getElementById("start-button"),
    pause: document.getElementById("pause-button"),
    restart: document.getElementById("restart-button"),
    mute: document.getElementById("mute-button"),
    announcement: document.getElementById("announcement"),
    left: document.getElementById("move-left"),
    right: document.getElementById("move-right"),
    fire: document.getElementById("fire-button")
  };

  let storageAvailable = true;
  function readStored(key, fallback) {
    try { return localStorage.getItem(key) ?? fallback; }
    catch { storageAvailable = false; return fallback; }
  }
  function writeStored(key, value) {
    try { localStorage.setItem(key, String(value)); }
    catch { storageAvailable = false; ui.recordNote.textContent = "Recorde desta sessão"; }
  }
  const storedBest = Number(readStored(BEST_KEY, "0"));
  let best = Number.isSafeInteger(storedBest) && storedBest > 0 ? storedBest : 0;
  let muted = readStored(MUTE_KEY, "false") === "true";
  let audioContext = null;
  let state = "ready";
  let score = 0;
  let wave = 1;
  let lives = 3;
  let gameTime = 0;
  let waveTime = 0;
  let shotTimer = 0;
  let diveTimer = 0;
  let nextWaveTimer = null;
  let announcementTimer = 0;
  let animationId = null;
  let previousFrame = null;
  let enemies = [];
  let playerShots = [];
  let enemyShots = [];
  let particles = [];
  const player = { x: WIDTH / 2, y: HEIGHT - 49, cooldown: 0, invincible: 0 };
  const keyboard = new Set();
  const pointers = new Map();
  const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  const stars = Array.from({ length: 76 }, () => ({
    x: Math.random() * WIDTH,
    y: Math.random() * HEIGHT,
    speed: 12 + Math.random() * 32,
    size: Math.random() > .85 ? 2 : 1,
    brightness: .2 + Math.random() * .5
  }));

  const shipSprite = [
    "......W......", ".....WWW.....", "..B..WWW..B..", "..B..WWW..B..",
    ".BB.WWWWW.BB.", ".BBWWWYWWWBB.", "BBBWWYYYWWBBB", "BBWBBBBBBBWWB",
    "BWWBB...BBWWB", "WWWB.....BWWW", ".WW.......WW."
  ];
  const alienSprites = [
    ["...OO.OO...", "..OOOOOOO..", ".OOOOWOOOO.", "OOOWWWWOOOO", "OOOOOOOOOOO", "..OOO.OOO..", ".OO.....OO.", "OO.......OO"],
    ["..B.....B..", "...B...B...", "..BBBBBBB..", ".BBWBBB WBB.", "BBBBBBBBBBB", "B.BBBBBBB.B", "B.B.....B.B", "...BB.BB..."].map(row => row.replace(/ /g, "")),
    ["....PPP....", "..PPPPPPP..", ".PPWPPPWPP.", "PPPPPPPPPPP", "P.PPPPPPP.P", "..PP.P.PP..", ".PP.....PP.", "P.........P"]
  ];
  const palettes = {
    ship: { W: "#edf9ff", B: "#76e5ed", Y: "#ffbc75" },
    alien: { O: "#ffab69", B: "#74dbe9", P: "#b799ff", W: "#f4f8ff" }
  };

  function prepareAudio() {
    if (muted) return;
    try {
      if (!audioContext) {
        const Audio = window.AudioContext || window.webkitAudioContext;
        if (Audio) audioContext = new Audio();
      }
      if (audioContext?.state === "suspended") audioContext.resume().catch(() => {});
    } catch { audioContext = null; }
  }

  function sound(frequency, duration, endFrequency = frequency, volume = .025) {
    if (muted || !audioContext || audioContext.state !== "running") return;
    try {
      const oscillator = audioContext.createOscillator();
      const gain = audioContext.createGain();
      const now = audioContext.currentTime;
      oscillator.type = "square";
      oscillator.frequency.setValueAtTime(frequency, now);
      oscillator.frequency.exponentialRampToValueAtTime(Math.max(20, endFrequency), now + duration);
      gain.gain.setValueAtTime(volume, now);
      gain.gain.exponentialRampToValueAtTime(.0001, now + duration);
      oscillator.connect(gain);
      gain.connect(audioContext.destination);
      oscillator.start(now);
      oscillator.stop(now + duration);
      oscillator.onended = () => { oscillator.disconnect(); gain.disconnect(); };
    } catch { /* Audio is optional: gameplay continues if unavailable. */ }
  }

  function updateHud() {
    ui.score.textContent = String(score).padStart(6, "0");
    ui.wave.textContent = String(wave).padStart(2, "0");
    ui.lives.textContent = lives > 0 ? "♥ ".repeat(lives).trim() : "—";
    ui.lives.setAttribute("aria-label", `${lives} ${lives === 1 ? "vida" : "vidas"}`);
    ui.best.textContent = String(best).padStart(6, "0");
  }

  function updateControls() {
    ui.pause.disabled = state !== "playing" && state !== "paused";
    ui.pause.textContent = state === "paused" ? "Continuar" : "Pausar";
    ui.restart.disabled = state === "ready";
    for (const button of [ui.left, ui.right, ui.fire]) button.disabled = state !== "playing";
    ui.mute.textContent = muted ? "Som desligado" : "Som ligado";
    ui.mute.setAttribute("aria-pressed", String(muted));
  }

  function saveBest() {
    // Another game tab may have set a better record since this one was opened.
    const persisted = Number(readStored(BEST_KEY, "0"));
    const persistedBest = Number.isSafeInteger(persisted) && persisted > 0 ? persisted : 0;
    if (persistedBest > best) { best = persistedBest; updateHud(); }
    if (best > persistedBest) writeStored(BEST_KEY, best);
  }
  function addScore(points) {
    score += points;
    best = Math.max(best, score);
    updateHud();
  }

  function clearInput() {
    keyboard.clear();
    pointers.clear();
    for (const button of [ui.left, ui.right, ui.fire]) button.classList.remove("is-held");
  }

  function announce(message, duration = 1.8) {
    ui.announcement.textContent = message;
    announcementTimer = duration;
  }

  function showOverlay(eyebrow, title, copy, button, hint) {
    ui.eyebrow.textContent = eyebrow;
    ui.title.textContent = title;
    ui.copy.textContent = copy;
    ui.start.textContent = button;
    ui.hint.textContent = hint;
    ui.overlay.hidden = false;
    ui.announcement.textContent = "";
  }

  function beginWave() {
    enemies = [];
    enemyShots = [];
    playerShots = [];
    waveTime = 0;
    shotTimer = 2.5;
    diveTimer = 2.8;
    nextWaveTimer = null;
    const rows = wave >= 4 ? 4 : 3;
    for (let row = 0; row < rows; row++) {
      for (let column = 0; column < 8; column++) {
        const hp = row === 0 && wave >= 2 ? 2 : 1;
        enemies.push({
          baseX: 51 + column * 54,
          baseY: 75 + row * 43,
          x: 51 + column * 54,
          y: -80 - row * 30,
          row, column, hp, maxHp: hp,
          type: row % 3,
          dive: null,
          flash: 0,
          entryDuration: 1.55 + column * .055 + row * .1
        });
      }
    }
    announce(`ONDA ${String(wave).padStart(2, "0")}`);
    updateHud();
    sound(330, .3, 880, .035);
  }

  function startGame() {
    saveBest();
    prepareAudio();
    stopAnimation();
    clearInput();
    state = "playing";
    score = 0;
    wave = 1;
    lives = 3;
    gameTime = 0;
    particles = [];
    player.x = WIDTH / 2;
    player.cooldown = 0;
    player.invincible = 1.6;
    ui.overlay.hidden = true;
    beginWave();
    updateControls();
    canvas.focus({ preventScroll: true });
    startAnimation();
  }

  function stopAnimation() {
    if (animationId !== null) cancelAnimationFrame(animationId);
    animationId = null;
    previousFrame = null;
  }

  function startAnimation() {
    if (animationId === null && state === "playing" && !document.hidden) {
      previousFrame = null;
      animationId = requestAnimationFrame(frame);
    }
  }

  function pauseGame(automatic = false) {
    if (state !== "playing") return;
    state = "paused";
    stopAnimation();
    clearInput();
    saveBest();
    showOverlay("MISSÃO EM ESPERA", "Pausa na órbita", automatic ? "Sua missão ficou guardada. Continue quando estiver pronto." : "Respire um pouco. A galáxia pode esperar.", "Continuar missão →", "Pressione P para continuar");
    updateControls();
    render();
  }

  function resumeGame() {
    if (state !== "paused" || document.hidden) return;
    prepareAudio();
    clearInput();
    state = "playing";
    ui.overlay.hidden = true;
    updateControls();
    canvas.focus({ preventScroll: true });
    startAnimation();
  }

  function gameOver() {
    state = "gameover";
    stopAnimation();
    clearInput();
    saveBest();
    showOverlay("FIM DA MISSÃO", "Até a próxima órbita", `${score.toLocaleString("pt-BR")} pontos · Você chegou à onda ${wave}.`, "Jogar novamente →", "Mais uma tentativa, mais uma galáxia");
    updateControls();
  }

  function burst(x, y, color, count = 12) {
    for (let index = 0; index < count && particles.length < 160; index++) {
      const angle = Math.random() * Math.PI * 2;
      const speed = 35 + Math.random() * 135;
      particles.push({ x, y, vx: Math.cos(angle) * speed, vy: Math.sin(angle) * speed, life: .3 + Math.random() * .4, total: .7, color, size: 2 + Math.random() * 3 });
    }
  }

  function hitPlayer() {
    if (player.invincible > 0 || state !== "playing") return;
    lives--;
    burst(player.x, player.y, "#ffb178", 24);
    sound(180, .4, 30, .05);
    player.invincible = 2.8;
    updateHud();
    if (lives <= 0) gameOver();
  }

  function enemyFire(enemy, aimed = false) {
    if (enemyShots.length >= 42 || enemy.y > HEIGHT - 130 || enemy.y < 0) return;
    const speed = Math.min(320, 175 + wave * 9);
    const difference = player.x - enemy.x;
    enemyShots.push({ x: enemy.x, y: enemy.y + 15, vx: aimed ? Math.max(-85, Math.min(85, difference * .35)) : 0, vy: speed });
  }

  const held = action => [...pointers.values()].includes(action);
  const lerp = (from, to, amount) => from + (to - from) * amount;
  const clamp = (value, low, high) => Math.max(low, Math.min(high, value));

  function update(dt) {
    gameTime += dt;
    waveTime += dt;
    player.invincible = Math.max(0, player.invincible - dt);
    player.cooldown = Math.max(0, player.cooldown - dt);
    const left = keyboard.has("ArrowLeft") || keyboard.has("KeyA") || held("left");
    const right = keyboard.has("ArrowRight") || keyboard.has("KeyD") || held("right");
    player.x = clamp(player.x + (Number(right) - Number(left)) * 305 * dt, 23, WIDTH - 23);

    if ((keyboard.has("Space") || held("fire")) && player.cooldown <= 0) {
      playerShots.push({ x: player.x, y: player.y - 19, vy: -510 });
      player.cooldown = .155;
      sound(900, .065, 290, .013);
    }

    if (announcementTimer > 0) {
      announcementTimer -= dt;
      if (announcementTimer <= 0) ui.announcement.textContent = "";
    }

    for (const enemy of enemies) {
      enemy.flash = Math.max(0, enemy.flash - dt);
      const homeX = enemy.baseX + Math.sin(gameTime * .85) * 14;
      const homeY = enemy.baseY + Math.sin(gameTime * 1.2 + enemy.column * .5) * 4;
      if (enemy.dive) {
        const dive = enemy.dive;
        dive.elapsed += dt;
        const progress = dive.elapsed / dive.duration;
        if (progress < .79) {
          const descent = progress / .79;
          enemy.x = lerp(dive.x, dive.targetX, descent) + Math.sin(descent * Math.PI * 2) * dive.direction * 75;
          enemy.y = lerp(dive.y, HEIGHT + 40, descent * descent * .55 + descent * .45);
          if (!dive.fired && descent > .38) { enemyFire(enemy, true); dive.fired = true; }
        } else if (progress < 1) {
          const returning = (progress - .79) / .21;
          enemy.x = lerp(clamp(dive.targetX, 25, WIDTH - 25), homeX, returning);
          enemy.y = lerp(-40, homeY, returning);
        } else {
          enemy.dive = null;
          enemy.x = homeX;
          enemy.y = homeY;
        }
      } else {
        const entry = Math.min(1, waveTime / enemy.entryDuration);
        const eased = 1 - Math.pow(1 - entry, 3);
        enemy.x = homeX;
        enemy.y = lerp(-100 - enemy.row * 30, homeY, eased);
      }
    }

    if (waveTime > 2.4 && enemies.length > 0) {
      shotTimer -= dt;
      diveTimer -= dt;
      if (shotTimer <= 0) {
        const candidates = enemies.filter(enemy => !enemy.dive);
        if (candidates.length > 0) enemyFire(candidates[Math.floor(Math.random() * candidates.length)], wave > 2);
        shotTimer = Math.max(.42, 1.15 - wave * .055) * (.8 + Math.random() * .4);
      }
      if (diveTimer <= 0) {
        const diving = enemies.filter(enemy => enemy.dive).length;
        const candidates = enemies.filter(enemy => !enemy.dive);
        if (diving < Math.min(5, 1 + Math.ceil(wave / 2)) && candidates.length > 0) {
          const enemy = candidates[Math.floor(Math.random() * candidates.length)];
          enemy.dive = { x: enemy.x, y: enemy.y, targetX: clamp(player.x + (Math.random() - .5) * 70, 35, WIDTH - 35), direction: Math.random() > .5 ? 1 : -1, duration: Math.max(2.9, 4.5 - wave * .12), elapsed: 0, fired: false };
        }
        diveTimer = Math.max(.5, 1.5 - wave * .07);
      }
    }

    for (const shot of playerShots) shot.y += shot.vy * dt;
    for (const shot of enemyShots) { shot.x += shot.vx * dt; shot.y += shot.vy * dt; }
    for (const particle of particles) {
      particle.x += particle.vx * dt;
      particle.y += particle.vy * dt;
      particle.vy += 70 * dt;
      particle.life -= dt;
    }

    for (const shot of playerShots) {
      if (shot.dead) continue;
      for (const enemy of enemies) {
        if (enemy.hp <= 0 || enemy.y < -15) continue;
        if (Math.abs(shot.x - enemy.x) < 18 && Math.abs(shot.y - enemy.y) < 16) {
          shot.dead = true;
          enemy.hp--;
          enemy.flash = .09;
          if (enemy.hp <= 0) {
            burst(enemy.x, enemy.y, ["#ffab69", "#74dbe9", "#b799ff"][enemy.type]);
            addScore(enemy.maxHp === 2 ? 150 : enemy.dive ? 100 : 80);
            sound(260, .1, 75, .028);
          } else { sound(440, .05, 260, .018); }
          break;
        }
      }
    }

    for (const shot of enemyShots) {
      if (Math.abs(shot.x - player.x) < 13 && Math.abs(shot.y - player.y) < 15) {
        shot.dead = true;
        hitPlayer();
      }
    }
    for (const enemy of enemies) {
      if (enemy.hp > 0 && Math.abs(enemy.x - player.x) < 24 && Math.abs(enemy.y - player.y) < 24) {
        // An invulnerable ship can pass through a formation safely after taking a hit.
        if (player.invincible <= 0) {
          enemy.hp = 0;
          burst(enemy.x, enemy.y, "#ffab69");
          hitPlayer();
        }
      }
    }

    enemies = enemies.filter(enemy => enemy.hp > 0);
    playerShots = playerShots.filter(shot => !shot.dead && shot.y > -20);
    enemyShots = enemyShots.filter(shot => !shot.dead && shot.y < HEIGHT + 20 && shot.x > -20 && shot.x < WIDTH + 20);
    particles = particles.filter(particle => particle.life > 0);
    if (state !== "playing") return;

    if (enemies.length === 0 && nextWaveTimer === null) {
      addScore(100 * wave);
      nextWaveTimer = 2;
      enemyShots = [];
      announce(`ÓRBITA LIVRE · +${100 * wave}`, 1.6);
      saveBest();
    }
    if (nextWaveTimer !== null) {
      nextWaveTimer -= dt;
      if (nextWaveTimer <= 0) {
        wave++;
        player.invincible = Math.max(player.invincible, 1.2);
        beginWave();
      }
    }
  }

  function drawSprite(sprite, x, y, scale, palette, flash = false) {
    const startX = Math.round(x - sprite[0].length * scale / 2);
    const startY = Math.round(y - sprite.length * scale / 2);
    for (let row = 0; row < sprite.length; row++) {
      for (let column = 0; column < sprite[row].length; column++) {
        const color = palette[sprite[row][column]];
        if (!color) continue;
        context.fillStyle = flash ? "#ffffff" : color;
        context.fillRect(startX + column * scale, startY + row * scale, scale, scale);
      }
    }
  }

  function render() {
    context.fillStyle = "#070c18";
    context.fillRect(0, 0, WIDTH, HEIGHT);
    for (const star of stars) {
      const y = (star.y + gameTime * star.speed) % HEIGHT;
      context.globalAlpha = star.brightness;
      context.fillStyle = "#bfdfff";
      context.fillRect(star.x, y, star.size, star.size);
    }
    context.globalAlpha = 1;

    context.strokeStyle = "#11203a";
    context.lineWidth = 1;
    context.beginPath();
    context.moveTo(16, HEIGHT - 18);
    context.lineTo(WIDTH - 16, HEIGHT - 18);
    context.stroke();

    for (const enemy of enemies) {
      drawSprite(alienSprites[enemy.type], enemy.x, enemy.y, 3, palettes.alien, enemy.flash > 0);
      if (enemy.hp > 1) {
        context.fillStyle = "#ffe5aa";
        context.fillRect(enemy.x - 5, enemy.y - 19, 3, 2);
        context.fillRect(enemy.x + 2, enemy.y - 19, 3, 2);
      }
    }
    for (const shot of playerShots) {
      context.fillStyle = "#76e5ed";
      context.fillRect(shot.x - 2, shot.y - 6, 4, 13);
      context.fillStyle = "#e6fcff";
      context.fillRect(shot.x - 1, shot.y - 6, 2, 8);
    }
    for (const shot of enemyShots) {
      context.fillStyle = "#ffaf73";
      context.fillRect(shot.x - 2, shot.y - 4, 4, 9);
      context.fillStyle = "#ffe6bc";
      context.fillRect(shot.x - 1, shot.y - 2, 2, 4);
    }

    if (lives > 0) {
      const blink = !reducedMotion && player.invincible > 0 && Math.floor(gameTime * 10) % 2 === 0;
      context.globalAlpha = blink ? .35 : 1;
      drawSprite(shipSprite, player.x, player.y, 3, palettes.ship);
      context.fillStyle = "#ffad64";
      context.fillRect(Math.round(player.x - 3), player.y + 17, 6, 6 + Math.floor(Math.sin(gameTime * 28) * 2));
      context.fillStyle = "#fff1c9";
      context.fillRect(Math.round(player.x - 1), player.y + 17, 2, 4);
      context.globalAlpha = 1;
      if (player.invincible > 0) {
        context.strokeStyle = "#72e6ed5c";
        context.beginPath();
        context.arc(player.x, player.y, 27, 0, Math.PI * 2);
        context.stroke();
      }
    }

    for (const particle of particles) {
      context.globalAlpha = Math.min(1, particle.life / .3);
      context.fillStyle = particle.color;
      context.fillRect(particle.x, particle.y, particle.size, particle.size);
    }
    context.globalAlpha = 1;
  }

  function frame(timestamp) {
    animationId = null;
    if (state !== "playing" || document.hidden) return;
    const dt = previousFrame === null ? 0 : Math.min(.05, Math.max(0, (timestamp - previousFrame) / 1000));
    previousFrame = timestamp;
    update(dt);
    render();
    if (state === "playing") animationId = requestAnimationFrame(frame);
  }

  function attachPointerControl(button, action) {
    button.addEventListener("pointerdown", event => {
      if (state !== "playing" || (event.pointerType === "mouse" && event.button !== 0)) return;
      event.preventDefault();
      prepareAudio();
      canvas.focus({ preventScroll: true });
      pointers.set(event.pointerId, action);
      button.classList.add("is-held");
      button.setPointerCapture(event.pointerId);
    });
    const release = event => {
      pointers.delete(event.pointerId);
      if (!held(action)) button.classList.remove("is-held");
    };
    button.addEventListener("pointerup", release);
    button.addEventListener("pointercancel", release);
    button.addEventListener("lostpointercapture", release);
    // A button activated by a keyboard still produces a useful short action.
    button.addEventListener("click", event => {
      if (state !== "playing" || event.detail !== 0) return;
      if (action === "fire") {
        playerShots.push({ x: player.x, y: player.y - 19, vy: -510 });
        sound(900, .065, 290, .013);
      } else { player.x = clamp(player.x + (action === "left" ? -28 : 28), 23, WIDTH - 23); }
      canvas.focus({ preventScroll: true });
    });
  }

  attachPointerControl(ui.left, "left");
  attachPointerControl(ui.right, "right");
  attachPointerControl(ui.fire, "fire");

  const gameKeyCodes = new Set(["ArrowLeft", "ArrowRight", "KeyA", "KeyD", "Space", "KeyP", "Escape"]);
  function normalizeKeyCode(event) {
    if (gameKeyCodes.has(event.code)) return event.code;
    // Unicode input and some keyboard layouts omit the physical key code.
    const key = typeof event.key === "string" ? event.key.toLowerCase() : "";
    const keyCodes = { a: "KeyA", d: "KeyD", p: "KeyP", " ": "Space", spacebar: "Space", space: "Space", arrowleft: "ArrowLeft", arrowright: "ArrowRight", escape: "Escape", esc: "Escape" };
    return keyCodes[key] ?? "";
  }

  document.addEventListener("keydown", event => {
    if (event.ctrlKey || event.altKey || event.metaKey) return;
    const code = normalizeKeyCode(event);
    const isButton = event.target?.closest?.("button, a");
    if (code === "KeyP" || code === "Escape") {
      if (state !== "playing" && state !== "paused") return;
      event.preventDefault();
      if (event.repeat) return;
      if (state === "playing") pauseGame();
      else if (code === "KeyP") resumeGame();
      return;
    }
    if (code === "Space" && !isButton && (state === "ready" || state === "gameover")) {
      event.preventDefault();
      if (!event.repeat) startGame();
      return;
    }
    if (state !== "playing" || (isButton && code === "Space")) return;
    if (["ArrowLeft", "ArrowRight", "KeyA", "KeyD", "Space"].includes(code)) {
      event.preventDefault();
      keyboard.add(code);
    }
  });
  document.addEventListener("keyup", event => { keyboard.delete(normalizeKeyCode(event)); });

  ui.start.addEventListener("click", () => state === "paused" ? resumeGame() : startGame());
  ui.pause.addEventListener("click", () => state === "playing" ? pauseGame() : resumeGame());
  ui.restart.addEventListener("click", startGame);
  ui.mute.addEventListener("click", () => {
    muted = !muted;
    writeStored(MUTE_KEY, muted);
    if (!muted) prepareAudio();
    updateControls();
    if (state === "playing") canvas.focus({ preventScroll: true });
  });

  window.addEventListener("blur", () => { clearInput(); pauseGame(true); });
  document.addEventListener("visibilitychange", () => {
    if (document.hidden) {
      clearInput();
      pauseGame(true);
      if (audioContext?.state === "running") audioContext.suspend().catch(() => {});
    }
  });
  window.addEventListener("pagehide", saveBest);
  window.addEventListener("storage", event => {
    if (event.key !== BEST_KEY) return;
    const otherBest = Number(event.newValue);
    if (Number.isSafeInteger(otherBest) && otherBest > best) {
      best = otherBest;
      updateHud();
    }
  });

  if (!storageAvailable) ui.recordNote.textContent = "Recorde desta sessão";
  updateHud();
  updateControls();
  render();
})();
