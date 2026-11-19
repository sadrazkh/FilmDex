/* A faithful browser port of FilmDex's own matcher.
   Ported line for line from src/FilmDex.Core/Names/NameNormalizer.cs and
   src/FilmDex.Core/Matching/SimilarityScorer.cs, so the percentages shown on this page are the
   percentages the application shows. The only difference is the archive: here it is the fixed
   demo list below, not a folder on your disk. */

(function () {
  'use strict';

  // ---------------------------------------------------------------- normaliser

  var VIDEO_EXTENSIONS = ['.mkv', '.mp4', '.avi', '.m4v', '.mov', '.wmv', '.flv', '.ts', '.m2ts',
    '.mts', '.mpg', '.mpeg', '.webm', '.rmvb', '.rm', '.divx', '.vob', '.iso', '.ogm', '.asf', '.3gp'];
  var COMPANION_EXTENSIONS = ['.srt', '.sub', '.idx', '.ass', '.ssa', '.nfo', '.txt', '.jpg',
    '.png', '.part', '.torrent'];
  var KNOWN_EXTENSIONS = VIDEO_EXTENSIONS.concat(COMPANION_EXTENSIONS);

  var TOP_LEVEL_DOMAINS = ['com', 'net', 'org', 'ir', 'us', 'info', 'co', 'me', 'tv', 'biz', 'ws',
    'io', 'la', 'site', 'xyz', 'top', 'in', 'cc', 'download', 'click'];

  var ROMAN_NUMERALS = { ii: '2', iii: '3', iv: '4', vi: '6', vii: '7', viii: '8', ix: '9' };

  var RAW_RELEASE_TAGS = [
    '480p', '576p', '720p', '1080p', '1440p', '2160p', '4k', '8k', 'uhd', 'fhd', 'hd', 'sd',
    'hdr', 'hdr10', 'sdr', 'dv',
    'bluray', 'blueray', 'bdrip', 'brrip', 'bdremux', 'remux', 'webrip', 'webdl', 'web', 'dl',
    'hdtv', 'pdtv', 'dvdrip', 'dvdscr', 'dvd', 'hdrip', 'camrip', 'cam', 'telesync', 'r5',
    'vhsrip', 'hdcam', 'scr', 'amzn', 'nf', 'hulu', 'dsnp', 'atvp', 'itunes', 'ts', 'tc',
    'x264', 'x265', 'h264', 'h265', 'hevc', 'avc', 'xvid', 'divx', 'av1',
    '10bit', '8bit', 'hi10p', 'hi10',
    'aac', 'ac3', 'eac3', 'ddp', 'dts', 'dtshd', 'truehd', 'atmos', 'mp3', 'flac', 'opus',
    'dubbed', 'dubfa', 'softsub', 'hardsub', 'subbed', 'sample', 'trailer',
    'yify', 'yts', 'rarbg', 'evo', 'eztv', 'ettv', 'psa', 'anoxmous', 'ganool', 'd3g',
    'دوبله', 'فارسی', 'زیرنویس', 'سانسور', 'بدون', 'دانلود', 'نسخه', 'کیفیت',
    'هاردساب', 'سافتساب', 'اختصاصی'
  ];

  var LETTER_MAP = {
    'ي': 'ی', 'ى': 'ی', 'ئ': 'ی',
    'ك': 'ک',
    'ة': 'ه',
    'أ': 'ا', 'إ': 'ا', 'آ': 'ا', 'ٱ': 'ا',
    'ؤ': 'و'
  };

  function isLetterOrDigit(ch) {
    return /[\p{L}\p{Nd}]/u.test(ch);
  }

  /** Folds a name to the alphabet, digits and spacing the index uses. */
  function fold(value) {
    var out = '';

    for (var i = 0; i < value.length; i++) {
      var ch = value[i];
      var code = ch.charCodeAt(0);

      // Zero-width joiners and bidi marks: "قصه‌های" is two words.
      if (code === 0x200c || code === 0x200d || code === 0x200e || code === 0x200f) { out += ' '; continue; }
      if (ch === '&') { out += ' and '; continue; }
      if (ch === "'" || ch === '’' || ch === '`') { continue; }

      if (code >= 0x0660 && code <= 0x0669) { out += String.fromCharCode(48 + code - 0x0660); continue; }
      if (code >= 0x06f0 && code <= 0x06f9) { out += String.fromCharCode(48 + code - 0x06f0); continue; }
      if ((code >= 0x064b && code <= 0x0652) || code === 0x0670) { continue; } // Arabic diacritics

      var mapped = LETTER_MAP[ch] || ch;
      out += isLetterOrDigit(mapped) ? mapped.toLowerCase() : ' ';
    }

    return out.replace(/\s+/g, ' ').trimEnd();
  }

  var RELEASE_TAGS = RAW_RELEASE_TAGS.map(fold);

  function extensionOf(name) {
    var dot = name.lastIndexOf('.');
    if (dot <= 0) return '';
    return name.slice(dot);
  }

  function dropSitePrefix(tokens) {
    if (tokens.length >= 3 && tokens[0] === 'www' && TOP_LEVEL_DOMAINS.indexOf(tokens[2]) >= 0) return tokens.slice(3);
    if (tokens.length >= 2 && tokens[0] === 'www' && TOP_LEVEL_DOMAINS.indexOf(tokens[1]) >= 0) return tokens.slice(2);
    return tokens;
  }

  function readSeasonAndEpisode(tokens) {
    for (var i = 0; i < tokens.length; i++) {
      var token = tokens[i];

      var se = /^s(\d{1,2})e(\d{1,3})$/.exec(token);
      if (se) return { season: +se[1], episode: +se[2], index: i };

      var cross = /^(\d{1,2})x(\d{1,3})$/.exec(token);
      if (cross) return { season: +cross[1], episode: +cross[2], index: i };

      if (token === 'season' && i + 1 < tokens.length && /^\d+$/.test(tokens[i + 1])) {
        return { season: +tokens[i + 1], episode: null, index: i };
      }

      var short = /^s(\d{2})$/.exec(token);
      if (short) return { season: +short[1], episode: null, index: i };
    }

    return { season: null, episode: null, index: -1 };
  }

  function readYear(tokens) {
    var index = -1;
    for (var i = 0; i < tokens.length; i++) {
      if (/^(19|20)\d{2}$/.test(tokens[i])) index = i;
    }
    // A year in first position is the title — "2012", "1917" — not a release year.
    if (index <= 0) return { year: null, index: index };
    return { year: +tokens[index], index: index };
  }

  function parse(rawName, isFolder) {
    var raw = rawName || '';
    var body = raw;

    if (!isFolder) {
      var extension = extensionOf(raw);
      if (extension.length > 1 && KNOWN_EXTENSIONS.indexOf(extension.toLowerCase()) >= 0) {
        body = raw.slice(0, raw.length - extension.length);
      }
    }

    var rawNormalized = body.trim().toLowerCase();
    var tokens = fold(body).split(' ').filter(function (t) { return t.length > 0; });
    tokens = dropSitePrefix(tokens);

    var se = readSeasonAndEpisode(tokens);
    var yr = readYear(tokens);

    var cut = tokens.length;
    if (yr.index > 0) cut = Math.min(cut, yr.index);
    if (se.index > 0) cut = Math.min(cut, se.index);

    var head = tokens.slice(0, cut);
    var kept = head
      .filter(function (t) { return RELEASE_TAGS.indexOf(t) < 0; })
      .map(function (t) { return ROMAN_NUMERALS[t] || t; });

    if (kept.length === 0) kept = head;
    if (kept.length === 0) kept = tokens;

    var cleanTitle = kept.join(' ');
    if (cleanTitle.length === 0) cleanTitle = rawNormalized;

    var distinct = kept.filter(function (t, i) { return kept.indexOf(t) === i; });
    var sorted = distinct.slice().sort();

    return {
      raw: raw,
      rawNormalized: rawNormalized,
      cleanTitle: cleanTitle,
      distinct: distinct,
      tokenSet: distinct.reduce(function (set, t) { set[t] = true; return set; }, {}),
      sortedTitle: sorted.join(' '),
      year: yr.year,
      season: se.season,
      episode: se.episode
    };
  }

  // ---------------------------------------------------------------- scorer

  var YEAR_BONUS = 5;
  var YEAR_PENALTY = 15;
  var CONTAINMENT_WEIGHT = 95;

  function levenshtein(left, right) {
    if (left.length === 0) return right.length;
    if (right.length === 0) return left.length;

    if (left.length > right.length) { var swap = left; left = right; right = swap; }

    var width = left.length + 1;
    var previous = new Array(width);
    var current = new Array(width);

    for (var i = 0; i < width; i++) previous[i] = i;

    for (var row = 1; row <= right.length; row++) {
      current[0] = row;
      var rightChar = right[row - 1];

      for (var column = 1; column < width; column++) {
        var cost = left[column - 1] === rightChar ? 0 : 1;
        current[column] = Math.min(current[column - 1] + 1, previous[column] + 1, previous[column - 1] + cost);
      }

      var hold = previous; previous = current; current = hold;
    }

    return previous[left.length];
  }

  function ratio(left, right) {
    if (left.length === 0 && right.length === 0) return 100;
    if (left.length === 0 || right.length === 0) return 0;
    if (left === right) return 100;

    var longest = Math.max(left.length, right.length);
    return Math.round((1 - levenshtein(left, right) / longest) * 100);
  }

  function append(common, rest) {
    var tail = rest.slice().sort();
    return tail.length === 0 ? common : common + ' ' + tail.join(' ');
  }

  function titlePercent(left, right) {
    var tokenSort = ratio(left.sortedTitle, right.sortedTitle);

    var shared = left.distinct.filter(function (t) { return right.tokenSet[t]; }).sort();
    if (shared.length === 0) return tokenSort;

    var common = shared.join(' ');
    var leftRest = append(common, left.distinct.filter(function (t) { return !right.tokenSet[t]; }));
    var rightRest = append(common, right.distinct.filter(function (t) { return !left.tokenSet[t]; }));

    var tokenSet = Math.max(ratio(common, leftRest), ratio(common, rightRest), ratio(leftRest, rightRest));

    return Math.max(tokenSort, Math.floor(tokenSet * CONTAINMENT_WEIGHT / 100));
  }

  function score(query, candidate) {
    var title = titlePercent(query, candidate);
    var yearMismatch = false;

    if (query.year !== null && candidate.year !== null) {
      if (query.year === candidate.year) {
        title = Math.min(100, title + YEAR_BONUS);
      } else {
        title = Math.max(0, title - YEAR_PENALTY);
        yearMismatch = true;
      }
    }

    return {
      titlePercent: title,
      rawPercent: ratio(query.rawNormalized, candidate.rawNormalized),
      yearMismatch: yearMismatch
    };
  }

  // ---------------------------------------------------------------- the demo archive

  var ARCHIVE = [
    'The.Matrix.1999.1080p.BluRay.x264-GROUP.mkv',
    'The.Matrix.Reloaded.2003.1080p.WEB-DL.x265.mkv',
    'Interstellar.2014.2160p.UHD.BluRay.x265-TERMINAL.mkv',
    'Blade.Runner.2049.2017.1080p.BluRay.x264.mkv',
    'Dune.Part.Two.2024.2160p.WEB-DL.DDP5.1.Atmos.HDR.H.265.mkv',
    'www.Film2Movie.Us.Inception.2010.720p.BluRay.mkv',
    'V.for.Vendetta.2005.1080p.BluRay.mkv',
    'Fast.and.Furious.2009.720p.BRRip.mkv',
    'Arrival.2016.1080p.BluRay.x264-SPARKS.mkv',
    'Fight.Club.1999.1080p.BluRay.x264.mkv',
    'Breaking.Bad.S01E02.720p.WEB-DL.mkv',
    'The.Lord.of.the.Rings.The.Fellowship.of.the.Ring.2001.EXTENDED.1080p.mkv',
    'قصه‌های ماشين‌ها ۲۰۰۶ دوبله فارسی.mkv'
  ].map(function (name) { return { name: name, parsed: parse(name, false) }; });

  // The application's own default threshold.
  var THRESHOLD = 60;

  // ---------------------------------------------------------------- wiring

  var input = document.getElementById('demo-input');
  var results = document.getElementById('demo-results');
  var parsedLine = document.getElementById('demo-parsed');
  if (!input || !results) return;

  function persianDigits(value) {
    return String(value).replace(/\d/g, function (d) { return '۰۱۲۳۴۵۶۷۸۹'[+d]; });
  }

  function band(percent) {
    if (percent >= 85) return 'high';
    if (percent >= THRESHOLD) return 'mid';
    return 'low';
  }

  function isPersian(text) {
    return /[؀-ۿ]/.test(text);
  }

  function render() {
    var text = input.value.trim();

    if (text.length === 0) {
      parsedLine.innerHTML = '';
      results.innerHTML = '<div class="demo-empty">اسم یک فیلم را بنویس تا آرشیو نمایشی را برایت بگردم.</div>';
      return;
    }

    var query = parse(text, false);

    var chips = '<span>آنچه برنامه از این اسم می‌فهمد:</span>' +
      '<span class="tag">' + escapeHtml(query.cleanTitle) + '</span>';
    if (query.year !== null) chips += '<span class="tag">سال ' + query.year + '</span>';
    if (query.season !== null) {
      chips += '<span class="tag">فصل ' + query.season +
        (query.episode !== null ? ' / قسمت ' + query.episode : '') + '</span>';
    }
    parsedLine.innerHTML = chips;

    var hits = ARCHIVE
      .map(function (entry) {
        return { entry: entry, score: score(query, entry.parsed) };
      })
      .filter(function (hit) {
        return Math.max(hit.score.titlePercent, hit.score.rawPercent) >= THRESHOLD;
      })
      .sort(function (a, b) {
        return (b.score.titlePercent - a.score.titlePercent) || (b.score.rawPercent - a.score.rawPercent);
      })
      .slice(0, 6);

    if (hits.length === 0) {
      results.innerHTML = '<div class="demo-empty"><b>چیزی شبیه این در آرشیو نیست.</b>' +
        'با خیال راحت دانلودش کن.</div>';
      return;
    }

    results.innerHTML = hits.map(function (hit) {
      var name = hit.entry.name;
      var titleClass = isPersian(name) ? 'result-name rtl' : 'result-name';
      var meta = 'نام خام ' + persianDigits(hit.score.rawPercent) + '٪';
      if (hit.score.yearMismatch) meta += ' · سال متفاوت';

      return '' +
        '<div class="result">' +
          '<div class="' + titleClass + '" title="' + escapeHtml(name) + '">' + escapeHtml(name) + '</div>' +
          '<div class="result-meta">' + meta + '</div>' +
          '<div style="display:flex;align-items:center;gap:12px">' +
            '<div class="result-bar"><i style="width:' + hit.score.titlePercent + '%"></i></div>' +
            '<span class="pct ' + band(hit.score.titlePercent) + '">' +
              persianDigits(hit.score.titlePercent) + '٪</span>' +
          '</div>' +
        '</div>';
    }).join('');
  }

  function escapeHtml(value) {
    return value.replace(/[&<>"']/g, function (ch) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[ch];
    });
  }

  input.addEventListener('input', render);

  Array.prototype.forEach.call(document.querySelectorAll('.preset'), function (button) {
    button.addEventListener('click', function () {
      input.value = button.dataset.query;
      render();
      input.focus();
    });
  });

  render();
})();
