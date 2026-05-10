(function () {
  'use strict';

  var API_BASE = window.__NEO_API_BASE__ || 'http://localhost:5000';
  var HUB_URL = API_BASE + '/hubs/game';
  var DEFAULT_ROOM_ID_KEY = 'neo_bridge_defaultRoomId';

  var _connection = null;
  var _currentRoomId = null;
  var _currentRoom = null;
  var _eventHandlers = {};
  var _characterCache = null;
  var _mapCache = null;

  function apiFetch(path, options) {
    var url = path.startsWith('http') ? path : API_BASE + '/' + path.replace(/^\//, '');
    return fetch(url, Object.assign({ headers: { 'Content-Type': 'application/json' } }, options || {}))
      .then(function (res) {
        if (!res.ok) {
          return res.text().then(function (text) {
            throw new Error('API error ' + res.status + ': ' + (text || res.statusText));
          });
        }
        if (res.status === 204) return null;
        return res.json();
      });
  }

  function apiPost(path, body) {
    return apiFetch(path, { method: 'POST', body: body ? JSON.stringify(body) : undefined });
  }

  function apiPatch(path, body) {
    return apiFetch(path, { method: 'PATCH', body: body ? JSON.stringify(body) : undefined });
  }

  function apiPut(path, body) {
    return apiFetch(path, { method: 'PUT', body: body ? JSON.stringify(body) : undefined });
  }

  function apiDelete(path) {
    return apiFetch(path, { method: 'DELETE' });
  }

  function getOrCreateRoomId() {
    if (_currentRoomId) return Promise.resolve(_currentRoomId);
    var stored = localStorage.getItem(DEFAULT_ROOM_ID_KEY);
    if (stored) {
      _currentRoomId = stored;
      return Promise.resolve(stored);
    }
    return createNewRoom();
  }

  function createNewRoom() {
    return apiPost('api/rooms', {
      roomName: 'Local BP Room',
      teamAName: 'Team A',
      teamBName: 'Team B'
    }).then(function (room) {
      _currentRoomId = room.roomId;
      _currentRoom = room;
      localStorage.setItem(DEFAULT_ROOM_ID_KEY, room.roomId);
      return room.roomId;
    });
  }

  function ensureRoom() {
    return getOrCreateRoomId().then(function (roomId) {
      return apiFetch('api/rooms/' + roomId).then(function (room) {
        if (room) {
          _currentRoom = room;
          _currentRoomId = room.roomId;
          return roomId;
        }
        localStorage.removeItem(DEFAULT_ROOM_ID_KEY);
        _currentRoomId = null;
        _currentRoom = null;
        return createNewRoom();
      }).catch(function () {
        localStorage.removeItem(DEFAULT_ROOM_ID_KEY);
        _currentRoomId = null;
        _currentRoom = null;
        return createNewRoom();
      });
    });
  }

  function ensureRoomSwitch(roomId) {
    if (!roomId) return Promise.resolve(null);
    _currentRoomId = roomId;
    _currentRoom = null;
    localStorage.setItem(DEFAULT_ROOM_ID_KEY, roomId);
    return apiFetch('api/rooms/' + roomId).then(function (room) {
      _currentRoom = room;
      syncRoomStateToUI(room);
      if (_connection) joinRoom(roomId);
      return room;
    });
  }

  function refreshRoomState() {
    if (!_currentRoomId) return Promise.resolve(_currentRoom);
    return apiFetch('api/rooms/' + _currentRoomId).then(function (room) {
      _currentRoom = room;
      syncRoomStateToUI(room);
      return room;
    }).catch(function () {
      return _currentRoom;
    });
  }

  function loadCharacters() {
    if (_characterCache) return Promise.resolve(_characterCache);
    return apiFetch('api/resources/characters').then(function (list) {
      _characterCache = list;
      return list;
    });
  }

  function loadMaps() {
    if (_mapCache) return Promise.resolve(_mapCache);
    return apiFetch('api/resources/maps').then(function (list) {
      _mapCache = list;
      return list;
    });
  }

  function findCharacterByName(name) {
    if (!name) return Promise.resolve(null);
    return loadCharacters().then(function (list) {
      for (var i = 0; i < list.length; i++) {
        var ch = list[i];
        var names = ch.names || {};
        if (ch.id === name || names['zh-CN'] === name || names['en-US'] === name) {
          return ch;
        }
      }
      return null;
    });
  }

  function findMapByName(name) {
    if (!name) return Promise.resolve(null);
    return loadMaps().then(function (list) {
      for (var i = 0; i < list.length; i++) {
        var m = list[i];
        var names = m.names || {};
        if (m.id === name || names['zh-CN'] === name || names['en-US'] === name) {
          return m;
        }
      }
      return null;
    });
  }

  function toCharacterImageUrl(character) {
    if (!character || !character.images || !character.images.length) return '';
    var primary = character.images.find(function (img) { return img.isPrimary; });
    return primary ? API_BASE + primary.url : API_BASE + character.images[0].url;
  }

  function initSignalR() {
    if (_connection) return Promise.resolve();
    return new Promise(function (resolve) {
      var script = document.createElement('script');
      script.src = API_BASE + '/runtime/layout-renderer/vendor/signalr.min.js';
      script.onload = function () {
        if (typeof signalR === 'undefined') {
          console.warn('[NeoBridge] signalR not available after script load');
          resolve();
          return;
        }
        _connection = new signalR.HubConnectionBuilder()
          .withUrl(HUB_URL)
          .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
          .build();

        _connection.on('RoomEvent', function (envelope) {
          var handlers = _eventHandlers['room-event'] || [];
          handlers.forEach(function (cb) { try { cb(envelope); } catch (e) { } });

          if (envelope.eventType === 'room.snapshot' || envelope.eventType === 'room.info.updated') {
            var snapshotHandlers = _eventHandlers['room-snapshot'] || [];
            snapshotHandlers.forEach(function (cb) { try { cb(envelope.payload); } catch (e) { } });
          }

          if (envelope.eventType === 'room.snapshot' && envelope.payload) {
            try {
              var roomData = typeof envelope.payload === 'string'
                ? JSON.parse(envelope.payload)
                : envelope.payload;
              _currentRoom = roomData;
              syncRoomStateToUI(roomData);
            } catch (e) { }
          }

          if (['room.role.selected', 'room.ban.updated', 'room.global-ban.updated',
               'room.map.updated', 'room.phase.updated', 'room.info.updated',
               'match.created'].indexOf(envelope.eventType) >= 0) {
            refreshRoomState();
          }
        });

        _connection.onreconnected(function () {
          if (_currentRoomId) {
            joinRoom(_currentRoomId);
          }
        });

        _connection.start()
          .then(function () {
            console.log('[NeoBridge] SignalR connected');
            resolve();
          })
          .catch(function (err) {
            console.warn('[NeoBridge] SignalR connection failed:', err);
            resolve();
          });
      };
      script.onerror = function () {
        console.warn('[NeoBridge] Failed to load SignalR client');
        resolve();
      };
      document.head.appendChild(script);
    });
  }

  function joinRoom(roomId) {
    if (!_connection) return Promise.resolve();
    return _connection.invoke('JoinRoom', roomId).then(function () {
      return _connection.invoke('ReplaceSubscriptions', roomId, [
        'room.snapshot', 'room.info.updated', 'match.created',
        'room.map.updated', 'room.ban.updated', 'room.global-ban.updated',
        'room.role.selected', 'room.phase.updated'
      ]);
    }).catch(function (err) {
      console.warn('[NeoBridge] JoinRoom failed:', err);
    });
  }

  function syncRoomStateToUI(room) {
    if (!room) return;
    if (typeof applyNeoRoomState === 'function') {
      applyNeoRoomState(room);
    }
  }

  function wrapSuccess(data) {
    return { success: true, data: data };
  }

  function getTeamId(teamSide) {
    if (!_currentRoom) return '';
    if (teamSide === 'teamA' && _currentRoom.teamA) return _currentRoom.teamA.id || '';
    if (teamSide === 'teamB' && _currentRoom.teamB) return _currentRoom.teamB.id || '';
    return '';
  }

  var neoBridge = {
    invoke: function (channel) {
      var args = Array.prototype.slice.call(arguments, 1);
      switch (channel) {
        case 'localBp:getCharacters':
          return loadCharacters().then(function (list) {
            var survivors = [];
            var hunters = [];
            list.forEach(function (ch) {
              var item = {
                id: ch.id,
                name: (ch.names && ch.names['zh-CN']) || ch.id,
                nameEn: (ch.names && ch.names['en-US']) || ch.id,
                role: ch.role,
                image: toCharacterImageUrl(ch)
              };
              if (ch.role === 'survivor' || ch.role === 'Survivor') survivors.push(item);
              else hunters.push(item);
            });
            return wrapSuccess({ survivors: survivors, hunters: hunters });
          });

        case 'character:get-index':
          return loadCharacters().then(function (list) {
            var idx = {};
            list.forEach(function (ch) {
              idx[ch.id] = ch;
              if (ch.names) {
                if (ch.names['zh-CN']) idx[ch.names['zh-CN']] = ch;
                if (ch.names['en-US']) idx[ch.names['en-US']] = ch;
              }
            });
            return wrapSuccess(idx);
          });

        case 'localBp:getState':
          return ensureRoom().then(function () {
            return refreshRoomState();
          }).then(function (room) {
            var data = convertRoomToState(room);
            return wrapSuccess(data);
          });

        case 'localBp:setSurvivor':
          return ensureRoom().then(function (roomId) {
            var index = args[0].index;
            var characterName = args[0].character;
            var slot = 'Survivor' + (index + 1);
            if (!characterName) {
              return apiPost('api/rooms/' + roomId + '/roles', {
                slot: slot,
                characterId: '',
                teamId: getTeamId('teamA')
              });
            }
            return findCharacterByName(characterName).then(function (ch) {
              if (!ch) throw new Error('Character not found: ' + characterName);
              return apiPost('api/rooms/' + roomId + '/roles', {
                slot: slot,
                characterId: ch.id,
                teamId: getTeamId('teamA')
              });
            });
          }).then(function () {
            return refreshRoomState();
          }).then(function (room) {
            return wrapSuccess(convertRoomToState(room));
          });

        case 'localBp:setHunter':
          return ensureRoom().then(function (roomId) {
            var characterName = args[0];
            if (!characterName) {
              return apiPost('api/rooms/' + roomId + '/roles', {
                slot: 'Hunter',
                characterId: '',
                teamId: getTeamId('teamB')
              });
            }
            return findCharacterByName(characterName).then(function (ch) {
              if (!ch) throw new Error('Character not found: ' + characterName);
              return apiPost('api/rooms/' + roomId + '/roles', {
                slot: 'Hunter',
                characterId: ch.id,
                teamId: getTeamId('teamB')
              });
            });
          }).then(function () {
            return refreshRoomState();
          }).then(function (room) {
            return wrapSuccess(convertRoomToState(room));
          });

        case 'localBp:addBanSurvivor':
          return ensureRoom().then(function (roomId) {
            return findCharacterByName(args[0]).then(function (ch) {
              if (!ch) return;
              return apiPost('api/rooms/' + roomId + '/bans', {
                role: 'Survivor',
                characterId: ch.id
              });
            });
          }).then(function () {
            return refreshRoomState();
          });

        case 'localBp:addBanHunter':
          return ensureRoom().then(function (roomId) {
            return findCharacterByName(args[0]).then(function (ch) {
              if (!ch) return;
              return apiPost('api/rooms/' + roomId + '/bans', {
                role: 'Hunter',
                characterId: ch.id
              });
            });
          }).then(function () {
            return refreshRoomState();
          });

        case 'localBp:removeBanSurvivor':
          return ensureRoom().then(function (roomId) {
            return findCharacterByName(args[0]).then(function (ch) {
              if (!ch) return;
              return apiPost('api/rooms/' + roomId + '/bans', {
                role: 'Survivor',
                characterId: ch.id,
                remove: true
              });
            });
          }).then(function () {
            return refreshRoomState();
          });

        case 'localBp:removeBanHunter':
          return ensureRoom().then(function (roomId) {
            return findCharacterByName(args[0]).then(function (ch) {
              if (!ch) return;
              return apiPost('api/rooms/' + roomId + '/bans', {
                role: 'Hunter',
                characterId: ch.id,
                remove: true
              });
            });
          }).then(function () {
            return refreshRoomState();
          });

        case 'localBp:addGlobalBanSurvivor':
          return ensureRoom().then(function (roomId) {
            return findCharacterByName(args[0]).then(function (ch) {
              if (!ch) return;
              return apiPost('api/rooms/' + roomId + '/global-bans', {
                role: 'Survivor',
                characterId: ch.id
              });
            });
          }).then(function () {
            return refreshRoomState();
          });

        case 'localBp:addGlobalBanHunter':
          return ensureRoom().then(function (roomId) {
            return findCharacterByName(args[0]).then(function (ch) {
              if (!ch) return;
              return apiPost('api/rooms/' + roomId + '/global-bans', {
                role: 'Hunter',
                characterId: ch.id
              });
            });
          }).then(function () {
            return refreshRoomState();
          });

        case 'localBp:removeGlobalBanSurvivor':
          return ensureRoom().then(function (roomId) {
            return findCharacterByName(args[0]).then(function (ch) {
              if (!ch) return;
              return apiPost('api/rooms/' + roomId + '/global-bans', {
                role: 'Survivor',
                characterId: ch.id,
                remove: true
              });
            });
          }).then(function () {
            return refreshRoomState();
          });

        case 'localBp:removeGlobalBanHunter':
          return ensureRoom().then(function (roomId) {
            return findCharacterByName(args[0]).then(function (ch) {
              if (!ch) return;
              return apiPost('api/rooms/' + roomId + '/global-bans', {
                role: 'Hunter',
                characterId: ch.id,
                remove: true
              });
            });
          }).then(function () {
            return refreshRoomState();
          });

        case 'localBp:setGlobalBan':
          return ensureRoom().then(function (roomId) {
            var role = args[0];
            var names = args[1];
            if (!Array.isArray(names)) return;
            var charRole = role === 'survivor' ? 'Survivor' : 'Hunter';
            return Promise.all(names.map(function (name) {
              return findCharacterByName(name).then(function (ch) {
                if (!ch) return;
                return apiPost('api/rooms/' + roomId + '/global-bans', {
                  role: charRole,
                  characterId: ch.id
                });
              });
            }));
          }).then(function () {
            return refreshRoomState();
          });

        case 'localBp:setPlayerName':
          return ensureRoom().then(function (roomId) {
            var idx = args[0].index;
            var name = args[0].name;
            var slot = idx < 4 ? 'Survivor' + (idx + 1) : 'Hunter';
            var teamId = idx < 4 ? getTeamId('teamA') : getTeamId('teamB');
            return apiPost('api/rooms/' + roomId + '/roles', {
              slot: slot,
              playerName: name,
              teamId: teamId
            });
          }).then(function () {
            return refreshRoomState();
          });

        case 'localBp:setSurvivorTalents':
          try {
            localStorage.setItem('neo_talents_survivor_' + args[0].index, JSON.stringify(args[0].talents));
          } catch (e) { }
          return Promise.resolve(wrapSuccess(true));

        case 'localBp:setHunterTalents':
          try {
            localStorage.setItem('neo_talents_hunter', JSON.stringify(args[0]));
          } catch (e) { }
          return Promise.resolve(wrapSuccess(true));

        case 'localBp:setHunterSkills':
          try {
            localStorage.setItem('neo_skills_hunter', JSON.stringify(args[0]));
          } catch (e) { }
          return Promise.resolve(wrapSuccess(true));

        case 'localBp:reset':
          return ensureRoom().then(function (roomId) {
            return apiPatch('api/rooms/' + roomId + '/phase', { phase: 'Waiting' });
          }).then(function () {
            return refreshRoomState();
          });

        case 'localBp:triggerBlink':
          return Promise.resolve(wrapSuccess(true));

        case 'localBp:applyMatchBase':
          return ensureRoom().then(function (roomId) {
            var matchBase = args[0];
            var promises = [];
            if (matchBase.teamAName || matchBase.teamALogo) {
              promises.push(apiPatch('api/rooms/' + roomId + '/teams', {
                teamA: { name: matchBase.teamAName, logoData: matchBase.teamALogo }
              }));
            }
            if (matchBase.teamBName || matchBase.teamBLogo) {
              promises.push(apiPatch('api/rooms/' + roomId + '/teams', {
                teamB: { name: matchBase.teamBName, logoData: matchBase.teamBLogo }
              }));
            }
            if (matchBase.mapName) {
              promises.push(findMapByName(matchBase.mapName).then(function (m) {
                if (m) {
                  return apiPatch('api/rooms/' + roomId + '/map', {
                    mapId: m.id,
                    mapName: (m.names && m.names['zh-CN']) || m.id
                  });
                }
              }));
            }
            return Promise.all(promises);
          }).then(function () {
            return refreshRoomState();
          });

        case 'localBp:setDefaultImages':
          try {
            localStorage.setItem('neo_default_images', JSON.stringify(args[0]));
          } catch (e) { }
          return Promise.resolve(wrapSuccess(true));

        case 'localBp:updateScoreData':
          try {
            localStorage.setItem('neo_score_data', JSON.stringify(args[0]));
          } catch (e) { }
          return Promise.resolve(wrapSuccess(true));

        case 'localBp:savePostMatch':
          try {
            localStorage.setItem('neo_postmatch_data', JSON.stringify(args[0]));
          } catch (e) { }
          return Promise.resolve(wrapSuccess(true));

        case 'localBp:listRooms':
          return apiFetch('api/rooms').then(function (list) {
            return wrapSuccess(list || []);
          });

        case 'localBp:switchRoom':
          return ensureRoomSwitch(args[0]).then(function (room) {
            return wrapSuccess(convertRoomToState(room));
          });

        case 'localBp:createRoom':
          var opts = args[0] || {};
          return apiPost('api/rooms', {
            roomName: opts.roomName || 'Local BP Room',
            teamAName: opts.teamAName || 'Team A',
            teamBName: opts.teamBName || 'Team B'
          }).then(function (room) {
            _currentRoomId = room.roomId;
            _currentRoom = room;
            localStorage.setItem(DEFAULT_ROOM_ID_KEY, room.roomId);
            if (_connection) joinRoom(room.roomId);
            return wrapSuccess(convertRoomToState(room));
          });

        case 'localBp:deleteRoom':
          return apiDelete('api/rooms/' + args[0]).then(function () {
            if (_currentRoomId === args[0]) {
              localStorage.removeItem(DEFAULT_ROOM_ID_KEY);
              _currentRoomId = null;
              _currentRoom = null;
            }
            return wrapSuccess(true);
          });

        case 'localBp:openCharacterDisplay':
        case 'localBp:openCharacterModel3D':
        case 'localBp:openCharacterModelAR':
        case 'open-local-backend':
          return Promise.resolve(wrapSuccess(true));

        default:
          console.log('[NeoBridge] Unhandled invoke:', channel, args);
          return Promise.resolve(wrapSuccess(null));
      }
    },

    listMapAssets: function () {
      return loadMaps().then(function (list) {
        return list.map(function (m) {
          var names = m.names || {};
          return {
            id: m.id,
            name: names['zh-CN'] || m.id,
            nameEn: names['en-US'] || m.id,
            image: m.images && m.images.length ? API_BASE + m.images[0].url : ''
          };
        });
      });
    },

    selectTeamLogo: function () {
      return Promise.resolve({ canceled: true });
    },

    selectImageForSlot: function () {
      return Promise.resolve({ canceled: true });
    },

    selectLocalBpConsoleBackground: function () {
      return Promise.resolve({ canceled: true });
    },

    getLocalBpConsoleBackground: function () {
      try {
        var saved = localStorage.getItem('neo_console_bg');
        if (saved) {
          return Promise.resolve({ success: true, settings: JSON.parse(saved) });
        }
      } catch (e) { }
      return Promise.resolve({ success: true, settings: {} });
    },

    setLocalBpConsoleBackground: function (settings) {
      try {
        localStorage.setItem('neo_console_bg', JSON.stringify(settings));
      } catch (e) { }
      return Promise.resolve({ success: true, settings: settings });
    },

    sendToFrontend: function (data) {
      return Promise.resolve();
    },

    openScoreboard: function () {
      return Promise.resolve();
    },

    openScoreboardOverview: function () {
      return Promise.resolve();
    },

    openPostMatch: function () {
      return Promise.resolve();
    },

    on: function (event, callback) {
      if (!_eventHandlers[event]) _eventHandlers[event] = [];
      _eventHandlers[event].push(callback);
    },

    off: function (event, callback) {
      if (!_eventHandlers[event]) return;
      if (callback) {
        _eventHandlers[event] = _eventHandlers[event].filter(function (cb) { return cb !== callback; });
      } else {
        delete _eventHandlers[event];
      }
    }
  };

  function convertRoomToState(room) {
    if (!room) return null;
    var picks = room.characterPicks || {};
    var bans = room.bans || {};
    var globalBans = room.globalBans || {};

    var survivors = [null, null, null, null];
    var hunter = null;
    var playerNamesArr = ['', '', '', '', ''];

    if (picks.survivor1) {
      survivors[0] = picks.survivor1.characterId || null;
      playerNamesArr[0] = picks.survivor1.playerName || picks.survivor1.name || '';
    }
    if (picks.survivor2) {
      survivors[1] = picks.survivor2.characterId || null;
      playerNamesArr[1] = picks.survivor2.playerName || picks.survivor2.name || '';
    }
    if (picks.survivor3) {
      survivors[2] = picks.survivor3.characterId || null;
      playerNamesArr[2] = picks.survivor3.playerName || picks.survivor3.name || '';
    }
    if (picks.survivor4) {
      survivors[3] = picks.survivor4.characterId || null;
      playerNamesArr[3] = picks.survivor4.playerName || picks.survivor4.name || '';
    }
    if (picks.hunter) {
      hunter = picks.hunter.characterId || null;
      playerNamesArr[4] = picks.hunter.playerName || picks.hunter.name || '';
    }

    var survivorTalents = [[], [], [], []];
    var hunterTalents = [];
    var hunterSkills = [];
    try {
      for (var i = 0; i < 4; i++) {
        var st = localStorage.getItem('neo_talents_survivor_' + i);
        if (st) survivorTalents[i] = JSON.parse(st);
      }
      var ht = localStorage.getItem('neo_talents_hunter');
      if (ht) hunterTalents = JSON.parse(ht);
      var hs = localStorage.getItem('neo_skills_hunter');
      if (hs) hunterSkills = JSON.parse(hs);
    } catch (e) { }

    return {
      survivors: survivors,
      hunter: hunter,
      hunterBannedSurvivors: (bans.survivorBans || []).map(function (b) { return b.characterId; }),
      survivorBannedHunters: (bans.hunterBans || []).map(function (b) { return b.characterId; }),
      globalBannedSurvivors: (globalBans.survivorBans || []).map(function (b) { return b.characterId; }),
      globalBannedHunters: (globalBans.hunterBans || []).map(function (b) { return b.characterId; }),
      survivorTalents: survivorTalents,
      hunterTalents: hunterTalents,
      hunterSkills: hunterSkills,
      playerNames: playerNamesArr
    };
  }

  function applyNeoRoomState(room) {
    if (!room) return;
    try {
      if (typeof state === 'undefined') return;

      var data = convertRoomToState(room);
      if (!data) return;

      state.survivors = data.survivors;
      state.hunter = data.hunter;
      state.hunterBannedSurvivors = data.hunterBannedSurvivors;
      state.survivorBannedHunters = data.survivorBannedHunters;
      state.globalBannedSurvivors = data.globalBannedSurvivors;
      state.globalBannedHunters = data.globalBannedHunters;

      for (var i = 0; i < 5; i++) {
        if (typeof playerNames !== 'undefined') {
          playerNames[i] = data.playerNames[i] || '';
        }
        var input = document.getElementById('player-name-' + i);
        if (input && data.playerNames[i]) {
          input.value = data.playerNames[i];
        }
      }

      if (typeof updateUI === 'function') updateUI();
    } catch (e) {
      console.warn('[NeoBridge] applyNeoRoomState error:', e);
    }
  }

  window.applyNeoRoomState = applyNeoRoomState;
  window.electronAPI = neoBridge;

  window.addEventListener('DOMContentLoaded', function () {
    initSignalR().then(function () {
      return ensureRoom();
    }).then(function (roomId) {
      if (_connection && roomId) {
        return joinRoom(roomId);
      }
    }).then(function () {
      if (_connection && _currentRoomId) {
        return _connection.invoke('RequestRoomSnapshot', _currentRoomId).catch(function () { });
      }
    }).catch(function (err) {
      console.warn('[NeoBridge] Init error:', err);
    });
  });

  window.__NEO_BRIDGE__ = neoBridge;
})();
