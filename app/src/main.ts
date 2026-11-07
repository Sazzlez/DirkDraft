import '@fontsource-variable/inter';
import './ui/tokens.css';
import { mount } from 'svelte';
import App from './App.svelte';
import { markShell } from './platform/shell';

markShell();

const app = mount(App, { target: document.getElementById('app')! });

export default app;
