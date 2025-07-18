document.addEventListener('DOMContentLoaded', () => {
    console.log("DOM listo: inicializando reproductores");

    document.querySelectorAll('.custom-player').forEach(player => {
        const audioId = player.dataset.audioId;
        const audio = document.getElementById(audioId);
        if (!audio) {
            console.error("No se encontró el audio con ID:", audioId);
            return;
        }

        const playBtn = player.querySelector('.play-btn');
        const backBtn = player.querySelector('.backward-btn');
        const fwdBtn = player.querySelector('.forward-btn');
        const progress = player.querySelector('.progress');
        const timeLabel = player.querySelector('.time');
        const volumeSlider = player.querySelector('.volume');
        const muteBtn = player.querySelector('.mute-btn');

        let isDragging = false;
        let isReady = false;

        console.log(`Configurando audio ${audioId}`);

        audio.preload = 'metadata';

        audio.addEventListener('loadedmetadata', () => {
            isReady = true;
            console.log(`${audioId} metadata cargada. Duración: ${audio.duration}`);

            if (progress) {
                progress.max = audio.duration;
                progress.value = 0;
            }
            if (timeLabel) {
                timeLabel.textContent = `0:00 / ${formatTime(audio.duration)}`;
            }
        });

        audio.addEventListener('timeupdate', () => {
            if (progress && isReady && !isDragging) {
                progress.value = audio.currentTime;
                if (timeLabel) {
                    timeLabel.textContent = `${formatTime(audio.currentTime)} / ${formatTime(audio.duration)}`;
                }
            }
        });

        if (playBtn) playBtn.addEventListener('click', () => {
            if (!isReady) return console.warn(`${audioId} no está listo para reproducir`);
            document.querySelectorAll('audio').forEach(other => {
                if (other !== audio) other.pause();
            });
            audio.paused ? audio.play() : audio.pause();
            playBtn.textContent = audio.paused ? '▶️' : '⏸️';
        });

        if (backBtn) backBtn.addEventListener('click', () => {
            if (!isReady) return console.warn(`${audioId} no está listo para retroceder`);
            audio.currentTime = Math.max(0, audio.currentTime - 10);
            progress.value = audio.currentTime;
        });

        if (fwdBtn) fwdBtn.addEventListener('click', () => {
            if (!isReady) return console.warn(`${audioId} no está listo para adelantar`);
            audio.currentTime = Math.min(audio.duration, audio.currentTime + 10);
            progress.value = audio.currentTime;
        });

        if (progress) {
            progress.addEventListener('mousedown', () => isDragging = true);
            progress.addEventListener('mouseup', () => {
                if (!isReady) return;
                isDragging = false;
                audio.currentTime = parseFloat(progress.value);
            });
            progress.addEventListener('input', () => {
                if (timeLabel && isDragging) {
                    timeLabel.textContent = `${formatTime(progress.value)} / ${formatTime(audio.duration || 0)}`;
                }
            });
        }

        if (volumeSlider) volumeSlider.addEventListener('input', () => {
            audio.volume = volumeSlider.value;
            if (muteBtn) muteBtn.textContent = audio.volume === 0 ? '🔇' : '🔊';
        });

        if (muteBtn) muteBtn.addEventListener('click', () => {
            audio.muted = !audio.muted;
            muteBtn.textContent = audio.muted ? '🔇' : '🔊';
            if (volumeSlider) volumeSlider.value = audio.muted ? 0 : audio.volume;
        });
    });
});

function formatTime(seconds) {
    const m = Math.floor(seconds / 60);
    const s = Math.floor(seconds % 60);
    return `${m}:${s.toString().padStart(2, '0')}`;
}
