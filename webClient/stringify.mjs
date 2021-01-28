import fs from 'fs';

let json = JSON.parse(fs.readFileSync('src/appSettings.json', 'utf-8'));
console.log(JSON.stringify(json));
