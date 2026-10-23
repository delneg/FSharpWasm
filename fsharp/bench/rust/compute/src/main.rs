//! Rust port of compiler-qualification/runtime-performance/workloads/compute,
//! a baseline for the NetWasm C# and F# versions: same kernels, same
//! arguments, same output lines and exact checksums. Safe Rust, so array
//! accesses keep their bounds checks, as in .NET.
use std::time::Instant;

trait Kernel {
    fn reset(&mut self);
    fn run(&mut self);
    fn checksum(&self) -> i64;
}

#[derive(Clone, Copy, Default)]
struct Body {
    x: f64,
    y: f64,
    z: f64,
    vx: f64,
    vy: f64,
    vz: f64,
    mass: f64,
}

const SOLAR_MASS: f64 = 4.0 * std::f64::consts::PI * std::f64::consts::PI;
const DAYS_PER_YEAR: f64 = 365.24;
const TIME_STEP: f64 = 0.01;

fn planet(x: f64, y: f64, z: f64, vx: f64, vy: f64, vz: f64, mass: f64) -> Body {
    Body {
        x,
        y,
        z,
        vx: vx * DAYS_PER_YEAR,
        vy: vy * DAYS_PER_YEAR,
        vz: vz * DAYS_PER_YEAR,
        mass: mass * SOLAR_MASS,
    }
}

fn create_system() -> [Body; 5] {
    let mut bodies = [
        Body { mass: SOLAR_MASS, ..Default::default() },
        planet(4.84143144246472090e+00, -1.16032004402742839e+00, -1.03622044471123109e-01,
            1.66007664274403694e-03, 7.69901118419740425e-03, -6.90460016972063023e-05,
            9.54791938424326609e-04),
        planet(8.34336671824457987e+00, 4.12479856412430479e+00, -4.03523417114321381e-01,
            -2.76742510726862411e-03, 4.99852801234917238e-03, 2.30417297573763929e-05,
            2.85885980666130812e-04),
        planet(1.28943695621391310e+01, -1.51111514016986312e+01, -2.23307578892655734e-01,
            2.96460137564761618e-03, 2.37847173959480950e-03, -2.96589568540237556e-05,
            4.36624404335156298e-05),
        planet(1.53796971148509165e+01, -2.59193146099879641e+01, 1.79258772950371181e-01,
            2.68067772490389322e-03, 1.62824170038242295e-03, -9.51592254519715870e-05,
            5.15138902046611451e-05),
    ];
    let (mut px, mut py, mut pz) = (0.0, 0.0, 0.0);
    for body in &bodies {
        px += body.vx * body.mass;
        py += body.vy * body.mass;
        pz += body.vz * body.mass;
    }
    bodies[0].vx = -px / SOLAR_MASS;
    bodies[0].vy = -py / SOLAR_MASS;
    bodies[0].vz = -pz / SOLAR_MASS;
    bodies
}

#[inline(never)]
fn advance(bodies: &mut [Body], steps: i32) {
    for _ in 0..steps {
        for i in 0..bodies.len() {
            let (left, right) = bodies.split_at_mut(i + 1);
            let a = &mut left[i];
            for b in right.iter_mut() {
                let dx = a.x - b.x;
                let dy = a.y - b.y;
                let dz = a.z - b.z;
                let distance_squared = dx * dx + dy * dy + dz * dz;
                let magnitude = TIME_STEP / (distance_squared * distance_squared.sqrt());
                let a_scale = a.mass * magnitude;
                let b_scale = b.mass * magnitude;
                a.vx -= dx * b_scale;
                a.vy -= dy * b_scale;
                a.vz -= dz * b_scale;
                b.vx += dx * a_scale;
                b.vy += dy * a_scale;
                b.vz += dz * a_scale;
            }
        }
        for body in bodies.iter_mut() {
            body.x += TIME_STEP * body.vx;
            body.y += TIME_STEP * body.vy;
            body.z += TIME_STEP * body.vz;
        }
    }
}

#[inline(never)]
fn energy(bodies: &[Body]) -> f64 {
    let mut e = 0.0;
    for i in 0..bodies.len() {
        let a = &bodies[i];
        e += 0.5 * a.mass * (a.vx * a.vx + a.vy * a.vy + a.vz * a.vz);
        for b in &bodies[i + 1..] {
            let dx = a.x - b.x;
            let dy = a.y - b.y;
            let dz = a.z - b.z;
            e -= a.mass * b.mass / (dx * dx + dy * dy + dz * dz).sqrt();
        }
    }
    e
}

struct NBody {
    steps: i32,
    initial: [Body; 5],
    bodies: [Body; 5],
}

impl Kernel for NBody {
    fn reset(&mut self) {
        self.bodies = self.initial;
    }
    fn run(&mut self) {
        advance(&mut self.bodies, self.steps);
    }
    fn checksum(&self) -> i64 {
        energy(&self.bodies).to_bits() as i64
    }
}

const SORT_LENGTH: usize = 100_000;

fn swap(values: &mut [i32], a: isize, b: isize) {
    values.swap(a as usize, b as usize);
}

fn sort(values: &mut [i32], mut low: isize, mut high: isize) {
    while high - low >= 16 {
        let middle = low + (high - low) / 2;
        if values[middle as usize] < values[low as usize] { swap(values, middle, low); }
        if values[high as usize] < values[low as usize] { swap(values, high, low); }
        if values[high as usize] < values[middle as usize] { swap(values, high, middle); }
        let pivot = values[middle as usize];
        let mut i = low;
        let mut j = high;
        while i <= j {
            while values[i as usize] < pivot { i += 1; }
            while values[j as usize] > pivot { j -= 1; }
            if i <= j {
                swap(values, i, j);
                i += 1;
                j -= 1;
            }
        }
        if j - low < high - i {
            sort(values, low, j);
            low = i;
        } else {
            sort(values, i, high);
            high = j;
        }
    }
    for i in low + 1..=high {
        let value = values[i as usize];
        let mut j = i - 1;
        while j >= low && values[j as usize] > value {
            values[(j + 1) as usize] = values[j as usize];
            j -= 1;
        }
        values[(j + 1) as usize] = value;
    }
}

#[inline(never)]
fn sort_rounds(source: &[i32], values: &mut [i32], rounds: i32) -> i64 {
    let mut checksum = 0i64;
    for round in 0..rounds {
        for (v, s) in values.iter_mut().zip(source) {
            *v = s ^ round;
        }
        let high = values.len() as isize - 1;
        sort(values, 0, high);
        for &v in values.iter() {
            checksum = checksum.wrapping_mul(31).wrapping_add(v as i64);
        }
    }
    checksum
}

struct Sorting {
    rounds: i32,
    source: Vec<i32>,
    values: Vec<i32>,
    checksum: i64,
}

impl Kernel for Sorting {
    fn reset(&mut self) {
        self.checksum = 0;
    }
    fn run(&mut self) {
        self.checksum = sort_rounds(&self.source, &mut self.values, self.rounds);
    }
    fn checksum(&self) -> i64 {
        self.checksum
    }
}

fn xorshift(state: &mut u32) -> u32 {
    *state ^= *state << 13;
    *state ^= *state >> 17;
    *state ^= *state << 5;
    *state
}

#[inline(never)]
fn crc_passes(table: &[u32; 256], data: &[u8], passes: i32) -> u32 {
    let mut crc = 0u32;
    for _ in 0..passes {
        let mut value = !crc;
        for &byte in data {
            value = table[((value ^ byte as u32) & 0xFF) as usize] ^ (value >> 8);
        }
        crc = !value;
    }
    crc
}

struct Crc32 {
    passes: i32,
    table: [u32; 256],
    data: Vec<u8>,
    crc: u32,
}

impl Kernel for Crc32 {
    fn reset(&mut self) {
        self.crc = 0;
    }
    fn run(&mut self) {
        self.crc = crc_passes(&self.table, &self.data, self.passes);
    }
    fn checksum(&self) -> i64 {
        self.crc as i64
    }
}

fn kernel(name: &str, count: i32) -> Option<Box<dyn Kernel>> {
    match name {
        "nbody" => {
            let initial = create_system();
            Some(Box::new(NBody { steps: count, initial, bodies: [Body::default(); 5] }))
        }
        "sort" => {
            let mut state = 0x9E3779B9u32;
            let source = (0..SORT_LENGTH).map(|_| xorshift(&mut state) as i32).collect();
            Some(Box::new(Sorting { rounds: count, source, values: vec![0; SORT_LENGTH], checksum: 0 }))
        }
        "crc32" => {
            let mut table = [0u32; 256];
            for (i, entry) in table.iter_mut().enumerate() {
                let mut value = i as u32;
                for _ in 0..8 {
                    value = if value & 1 != 0 { 0xEDB88320 ^ (value >> 1) } else { value >> 1 };
                }
                *entry = value;
            }
            let mut state = 0x2545F491u32;
            let data = (0..1 << 20).map(|_| xorshift(&mut state) as u8).collect();
            Some(Box::new(Crc32 { passes: count, table, data, crc: 0 }))
        }
        _ => None,
    }
}

fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let count = match args.get(1).and_then(|s| s.parse::<i32>().ok()) {
        Some(c) if args.len() == 2 && c > 0 => c,
        _ => std::process::exit(2),
    };
    let Some(mut kernel) = kernel(&args[0], count) else { std::process::exit(3) };
    for _ in 0..3 {
        kernel.reset();
        kernel.run();
    }
    kernel.reset();
    let start = Instant::now();
    kernel.run();
    let elapsed = start.elapsed().as_nanos();
    // Rust has no GC: the GC line is kept for the parser, with zeros.
    println!("COMPUTE|{}|{}|1000000000|{}", args[0], elapsed, kernel.checksum());
    println!("GC|0|0|0");
}
