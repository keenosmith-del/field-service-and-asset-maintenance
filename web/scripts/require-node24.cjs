if (Number(process.versions.node.split('.')[0]) !== 24) {
  console.error('This project requires Node.js 24. Run `nvm use` in the project directory before installing, starting, or building.');
  process.exit(1);
}
