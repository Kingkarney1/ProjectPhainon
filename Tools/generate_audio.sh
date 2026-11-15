#!/usr/bin/env bash
# Synthesises the placeholder sound set for Operation Phainon with ffmpeg.
# Every clip is generated from oscillators and noise, so nothing here is sampled from a recording.
# File names match the fields of the AudioLibrary asset (engineLoop -> engine_loop.wav); the project
# builder assigns them automatically. Replace any file with recorded audio of the same name.
#
# Usage: Tools/generate_audio.sh [output directory]
set -euo pipefail

OUT="${1:-$(cd "$(dirname "$0")/.." && pwd)/Assets/Phainon/Audio}"
mkdir -p "$OUT"
FF=(ffmpeg -hide_banner -loglevel error -y)
FMT=(-ar 44100 -ac 1 -c:a pcm_s16le)

# One-shot from a single lavfi expression graph.
shot() { # name graph
  "${FF[@]}" -f lavfi -i "$2" "${FMT[@]}" "$OUT/$1.wav"
}

# Seamless loop from a noise-based graph: render length+fade seconds, then cross-fade the tail
# into the head so the end of the file flows into its start.
loop() { # name length fade graph
  local name="$1" len="$2" fade="$3" graph="$4"
  local total; total=$(awk "BEGIN{print $len+$fade}")
  "${FF[@]}" -f lavfi -i "$graph" -t "$total" -filter_complex "
    [0:a]asplit=3[a][b][c];
    [a]atrim=0:$fade,asetpts=PTS-STARTPTS,afade=t=in:st=0:d=$fade[head];
    [b]atrim=$len:$total,asetpts=PTS-STARTPTS,afade=t=out:st=0:d=$fade[tail];
    [head][tail]amix=inputs=2:normalize=0[blend];
    [c]atrim=$fade:$len,asetpts=PTS-STARTPTS[body];
    [blend][body]concat=n=2:v=0:a=1,alimiter=limit=0.95[out]" -map "[out]" "${FMT[@]}" "$OUT/$name.wav"
}

# ---------------------------------------------------------------- cockpit controls
shot button_press   "aevalsrc='0.55*sin(2*PI*210*t)*exp(-t*55)+0.35*(random(0)-0.5)*exp(-t*220)':d=0.09:s=44100,highpass=f=120"
shot switch_on      "aevalsrc='0.6*(random(0)-0.5)*exp(-t*380)+0.5*(random(1)-0.5)*exp(-max(t-0.022,0)*420)*gt(t,0.022)+0.25*sin(2*PI*2900*t)*exp(-t*160)':d=0.08:s=44100,highpass=f=900"
shot switch_off     "aevalsrc='0.6*(random(0)-0.5)*exp(-t*340)+0.45*(random(1)-0.5)*exp(-max(t-0.018,0)*380)*gt(t,0.018)+0.25*sin(2*PI*2100*t)*exp(-t*150)':d=0.08:s=44100,highpass=f=700"
shot dial_tick      "aevalsrc='0.5*(random(0)-0.5)*exp(-t*650)+0.2*sin(2*PI*3800*t)*exp(-t*500)':d=0.03:s=44100,highpass=f=1500"
shot guard_open     "aevalsrc='0.5*sin(2*PI*340*t)*exp(-t*45)+0.5*(random(0)-0.5)*exp(-t*140)+0.3*(random(1)-0.5)*exp(-max(t-0.07,0)*260)*gt(t,0.07)':d=0.16:s=44100,highpass=f=200"
shot guard_close    "aevalsrc='0.55*sin(2*PI*260*t)*exp(-t*50)+0.55*(random(0)-0.5)*exp(-t*170)':d=0.12:s=44100,highpass=f=160"
shot lever_move     "aevalsrc='0.4*(random(0)-0.5)*exp(-t*260)+0.3*sin(2*PI*900*t)*exp(-t*180)':d=0.05:s=44100,highpass=f=400,lowpass=f=5000"
shot denied         "aevalsrc='0.45*(sin(2*PI*150*t)+0.5*sin(2*PI*300*t)+0.3*sin(2*PI*450*t))*(lt(t,0.11)+gt(t,0.16))*min(1\,(0.27-t)*40)*min(1\,t*200)':d=0.27:s=44100,lowpass=f=2400"

# ---------------------------------------------------------------- events
shot power_up       "aevalsrc='(0.35*sin(2*PI*(55*t+38*t*t))+0.14*sin(2*PI*(220*t+150*t*t))+0.25*(random(0)-0.5)*exp(-t*90))*min(1\,t*6)*min(1\,(1.5-t)*2.5)':d=1.5:s=44100,lowpass=f=3000"
shot power_down     "aevalsrc='(0.35*sin(2*PI*(130*t-34*t*t))+0.12*sin(2*PI*(480*t-150*t*t)))*min(1\,t*10)*min(1\,(1.4-t)*1.4)+0.3*(random(0)-0.5)*exp(-t*120)':d=1.4:s=44100,lowpass=f=2600"
shot engine_ignition "anoisesrc=d=3.2:c=brown:r=44100:a=0.9:seed=11,volume='min(1,0.15+t*t*0.55)*min(1,(3.2-t)*2)':eval=frame,lowpass=f=1400,bass=g=9:f=90,alimiter=limit=0.95"
shot engine_cutoff  "anoisesrc=d=2.0:c=brown:r=44100:a=0.9:seed=12,volume='exp(-t*2.6)':eval=frame,lowpass=f=900,bass=g=8:f=80,alimiter=limit=0.95"
shot stage_separation "aevalsrc='0.9*(random(0)-0.5)*exp(-t*26)+0.6*sin(2*PI*62*t)*exp(-t*5)+0.35*sin(2*PI*410*t)*exp(-max(t-0.12,0)*14)*gt(t,0.12)+0.25*sin(2*PI*1270*t)*exp(-max(t-0.12,0)*22)*gt(t,0.12)':d=1.6:s=44100,lowpass=f=6000,alimiter=limit=0.95"
shot notification   "aevalsrc='0.32*sin(2*PI*880*t)*lt(t,0.11)*min(1\,t*300)*min(1\,(0.11-t)*90)+0.32*sin(2*PI*1320*t)*gt(t,0.13)*min(1\,(t-0.13)*300)*min(1\,(0.30-t)*30)':d=0.30:s=44100"
shot countdown_beep "aevalsrc='0.38*sin(2*PI*1000*t)*min(1\,t*400)*min(1\,(0.13-t)*80)':d=0.13:s=44100"
shot caution_chime  "aevalsrc='0.36*sin(2*PI*660*t)*exp(-t*7)*min(1\,t*300)+0.36*sin(2*PI*520*t)*exp(-max(t-0.24,0)*6)*gt(t,0.24)*min(1\,max(t-0.24,0)*300)':d=0.85:s=44100"
shot mission_complete "aevalsrc='0.26*(sin(2*PI*523.25*t)*exp(-t*2.4)+sin(2*PI*659.25*t)*exp(-max(t-0.22,0)*2.4)*gt(t,0.22)+sin(2*PI*783.99*t)*exp(-max(t-0.44,0)*2.4)*gt(t,0.44)+sin(2*PI*1046.5*t)*exp(-max(t-0.66,0)*1.8)*gt(t,0.66))*min(1\,(2.4-t)*3)':d=2.4:s=44100"
shot mission_failed "aevalsrc='0.30*(sin(2*PI*392*t)*exp(-t*3)+sin(2*PI*311.13*t)*exp(-max(t-0.38,0)*3)*gt(t,0.38)+sin(2*PI*233.08*t)*exp(-max(t-0.76,0)*2)*gt(t,0.76))*min(1\,(2.2-t)*3)':d=2.2:s=44100"

# ---------------------------------------------------------------- loops
loop cabin_ambient 6 1.0 "anoisesrc=c=pink:r=44100:a=0.5:seed=21,lowpass=f=820,highpass=f=70"
loop apu_loop      5 0.8 "anoisesrc=c=white:r=44100:a=0.22:seed=22,bandpass=f=2600:t=h:w=1800,aeval='val(0)+0.10*sin(2*PI*310*t)+0.06*sin(2*PI*620*t)+0.03*sin(2*PI*930*t)'"
loop engine_loop   6 1.0 "anoisesrc=c=brown:r=44100:a=1.0:seed=23,lowpass=f=1500,bass=g=8:f=85,tremolo=f=11:d=0.18,alimiter=limit=0.95"
loop rumble_loop   6 1.0 "anoisesrc=c=brown:r=44100:a=1.0:seed=24,lowpass=f=110,volume=3.2,tremolo=f=6.5:d=0.35,alimiter=limit=0.95"
loop wind_loop     6 1.0 "anoisesrc=c=pink:r=44100:a=0.7:seed=25,bandpass=f=900:t=h:w=1400,volume=2.2,tremolo=f=0.7:d=0.4"
loop rcs_loop      2 0.4 "anoisesrc=c=white:r=44100:a=0.5:seed=26,highpass=f=1800,lowpass=f=9000"

loop radio_static_loop 4 0.6 "anoisesrc=c=white:r=44100:a=0.5:seed=27,bandpass=f=2400:t=h:w=2600,tremolo=f=0.4:d=0.3"

# Periodic loops made of whole cycles need no cross-fade.
# One slow breath in and out every four seconds, heard from inside a helmet.
shot breathing_loop "aevalsrc='0.9*(random(0)-0.5)*(pow(sin(PI*t/1.6)\,2)*lt(t\,1.6)+0.8*pow(sin(PI*(t-2)/1.8)\,2)*gt(t\,2)*lt(t\,3.8))':d=4:s=44100,lowpass=f=1100,highpass=f=180"
shot electrical_hum "aevalsrc='0.20*sin(2*PI*400*t)+0.07*sin(2*PI*800*t)+0.04*sin(2*PI*1200*t)+0.10*sin(2*PI*120*t)':d=4:s=44100"
shot master_alarm   "aevalsrc='0.42*(sin(2*PI*940*t)+0.35*sin(2*PI*1880*t))*lt(mod(t\,0.5)\,0.24)*min(1\,mod(t\,0.5)*200)*min(1\,(0.24-mod(t\,0.5))*200)':d=1:s=44100"

echo "Wrote $(ls "$OUT"/*.wav | wc -l) clips to $OUT"
