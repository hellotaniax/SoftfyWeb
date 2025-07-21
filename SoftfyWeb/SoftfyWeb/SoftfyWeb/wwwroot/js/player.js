document.addEventListener('DOMContentLoaded', () => {
    const players = document.querySelectorAll('.audio-player');

    players.forEach(player => {
        const id = player.dataset.id;
        const audio = document.getElementById(`audio-${id}`);
        const playPauseBtn = player.querySelector('.play-pause');
        const progress = player.querySelector('.progress');
        const currentTimeEl = player.querySelector('.time.current');
        const durationTimeEl = player.querySelector('.time.duration');

        const formatTime = (seconds) => {
            const m = Math.floor(seconds / 60);
            const s = Math.floor(seconds % 60).toString().padStart(2, '0');
            return `${m}:${s}`;
        };

        audio.addEventListener('loadedmetadata', () => {
            durationTimeEl.textContent = formatTime(audio.duration);
            progress.max = Math.floor(audio.duration);
        });

        audio.addEventListener('timeupdate', () => {
            progress.value = Math.floor(audio.currentTime);
            currentTimeEl.textContent = formatTime(audio.currentTime);
        });

        audio.addEventListener('error', (e) => {
            console.error('Error al cargar audio:', e);
        });

        playPauseBtn.addEventListener('click', () => {
            document.querySelectorAll('audio').forEach(a => {
                if (a !== audio) {
                    a.pause();
                    const btn = a.closest('.audio-player').querySelector('.play-pause');
                    if (btn) btn.textContent = '▶️';
                }
            });

            if (audio.paused) {
                audio.play();
                playPauseBtn.textContent = '⏸️';
            } else {
                audio.pause();
                playPauseBtn.textContent = '▶️';
            }
        });

        progress.addEventListener('input', () => {
            audio.currentTime = progress.value;
        });
    });
});